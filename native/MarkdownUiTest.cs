using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    internal static class MarkdownUiTest {
        internal static async Task Run(MainWindow window) {
            string folder=TestArtifacts.PathFor("markdown-fixture");
            Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"README.md");
            string reference=Path.Combine(folder,"reference note.txt");
            string content="# Orbit 작업 노트\n\n에이전트와 함께하는 **편안한 작업 공간**입니다.\n\n[목차](#오늘의-작업)\n\n## 오늘의 작업\n\n- [x] 터미널을 원하는 위치로 이동\n- [ ] 다음 변경 사항 검토\n\n| 기능 | 상태 |\n| --- | --- |\n| 터미널 분할 | 준비됨 |\n| Markdown 미리보기 | 준비됨 |\n\n> 변경 내용은 작게 나누고, 결과를 확인하세요.\n\n```javascript\nconst workspace = 'Orbit';\nconsole.log(workspace);\n```\n\n[연결된 메모](reference%20note.txt) · [공식 문서](https://example.com/docs)\n\n[실행 금지](javascript:alert(1))\n\n<script>window.__orbitMarkdownInjected=true</script>\n\n![원격 이미지](https://example.com/track.png)\n";
            File.WriteAllText(path,content,new UTF8Encoding(false));
            File.WriteAllText(reference,"Markdown relative link resolved correctly.\n",new UTF8Encoding(false));
            var json=new JavaScriptSerializer();
            string edited=content+"\n## 저장 전 변경\n\nDraft revision stays intact.\n";
            await window.ExecuteForTest(Script.Replace("__DOCUMENT_PATH__",json.Serialize(path)).Replace("__REFERENCE_PATH__",json.Serialize(reference)).Replace("__EDITED_CONTENT__",json.Serialize(edited)));
            string result=null;
            for(int i=0;i<250;i++) {
                result=await window.ExecuteForTest("JSON.stringify(window.__orbitMarkdownTest || null)");
                if(result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)>=0)break;
                await Task.Delay(100);
            }
            File.WriteAllText(TestArtifacts.PathFor("markdown-report.json"),result??"null");
            if(result==null || result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)<0)throw new TimeoutException("Markdown UI test timed out");
            if(result.IndexOf("\\\"error\\\"",StringComparison.Ordinal)>=0)throw new InvalidOperationException(result);
            if(File.ReadAllText(path)!=edited)throw new InvalidOperationException("Markdown preview save did not preserve edited text");
            await window.CaptureForTest(TestArtifacts.PathFor("markdown-desktop.png"));
        }
        private const string Script=@"
window.__orbitMarkdownTest={done:false};
(async()=>{try{
    const check=(ok,message)=>{if(!ok)throw new Error(message);};
    const pause=ms=>new Promise(r=>setTimeout(r,ms));
    const wait=async test=>{for(let i=0;i<100;i++){if(test())return;await pause(40);}throw new Error('Markdown UI condition timed out');};
    const visible=node=>node&&node.getBoundingClientRect().height>0;
    const preview=()=>document.querySelector('#markdown-preview');
    const mode=async name=>{document.querySelector('[data-editor-mode='+name+']').click();await wait(()=>name==='preview'?visible(preview()):visible(document.querySelector('#editor-host .cm-editor')));};
    const path=__DOCUMENT_PATH__,reference=__REFERENCE_PATH__,edited=__EDITED_CONTENT__;
    await window.orbitUiTest.openFile(path);
    await wait(()=>visible(preview()));
    await pause(100);
    check(document.querySelector('.topbar').getBoundingClientRect().y>=0,'opening Markdown scrolled the window header offscreen');
    check(preview().querySelector('h1')?.textContent==='Orbit 작업 노트','Markdown headings did not render');
    check(preview().querySelectorAll('table tbody tr').length===2,'Markdown table did not render');
    check(preview().querySelector('pre code')?.textContent.includes('const workspace'),'fenced code block did not render');
    check(preview().querySelectorAll('input[type=checkbox]').length===2,'task list did not render');
    check(!window.__orbitMarkdownInjected&&!preview().querySelector('script,iframe,object,embed'),'Markdown executed embedded HTML');
    check(![...preview().querySelectorAll('a')].some(a=>/^javascript:/i.test(a.getAttribute('href')||'')),'unsafe Markdown link remained clickable');
    check(!preview().querySelector('img[src^=http]'),'opening Markdown fetched remote images automatically');
    check(preview().querySelector('h2')?.id==='오늘의-작업','Markdown heading anchor is missing');
    [...preview().querySelectorAll('a')].find(a=>a.textContent==='목차').click();await pause(100);
    check(preview().scrollTop>0,'Markdown table of contents did not scroll the document');
    await mode('edit');
    await window.orbitUiTest.editContent(edited);
    check(window.orbitDiagnostics().files.some(f=>f.name==='README.md'&&f.dirty),'Markdown edit was not marked unsaved');
    await mode('preview');
    check(preview().textContent.includes('Draft revision stays intact.'),'preview lost the unsaved edit');
    await window.orbitUiTest.openFile(reference);
    await window.orbitUiTest.openFile(path);
    await wait(()=>visible(preview()));
    check(preview().textContent.includes('Draft revision stays intact.'),'switching files lost the unsaved Markdown edit');
    await window.orbitUiTest.save();
    check(!window.orbitDiagnostics().files.some(f=>f.name==='README.md'&&f.dirty),'saving from preview left Markdown dirty');
    [...preview().querySelectorAll('a')].find(a=>a.textContent==='연결된 메모').click();
    await wait(()=>document.querySelector('#editor-path').textContent===reference);
    await window.orbitUiTest.openFile(path);await wait(()=>visible(preview()));
    const oldFirst=document.querySelector('#file-tree').firstElementChild;
    document.querySelector('#tree-actions button').click();
    await wait(()=>document.querySelector('#file-tree').firstElementChild!==oldFirst && [...document.querySelectorAll('.tree-directory')].some(row=>row.title.endsWith('markdown-fixture')));
    const folder=[...document.querySelectorAll('.tree-directory')].find(row=>row.title.endsWith('markdown-fixture'));
    const siblings=[...folder.parentElement.children].filter(row=>row.classList.contains('tree-row'));
    folder.click();await wait(()=>folder.nextElementSibling?.classList.contains('tree-children'));
    await wait(()=>[...folder.nextElementSibling.querySelectorAll('.tree-file')].some(row=>row.title===path));
    const selected=[...folder.nextElementSibling.querySelectorAll('.tree-file')].find(row=>row.title===path);
    check(selected.classList.contains('selected'),'explorer does not highlight the active file');
    const referenceRow=[...folder.nextElementSibling.querySelectorAll('.tree-file')].find(row=>row.title===reference);
    referenceRow.click();await wait(()=>document.querySelector('#editor-path').textContent===reference);
    check(referenceRow.classList.contains('selected')&&!selected.classList.contains('selected'),'explorer selection did not follow the newly opened file');
    await window.orbitUiTest.openFile(path);await wait(()=>visible(preview()));
    folder.click();await pause(60);
    check(siblings.every(row=>row.isConnected),'collapsing a folder removed a sibling entry');
    folder.click();await wait(()=>folder.nextElementSibling?.querySelector('.tree-file'));
    window.__orbitMarkdownTest={done:true,checks:['headings and table','task list','fenced code','inert raw HTML','safe links','no automatic remote images','edit/preview/file switch preservation','save from preview','document-relative link','explorer active file and folder collapse']};
}catch(e){window.__orbitMarkdownTest={done:true,error:String(e&&e.stack||e)};}})();
'started';";
    }
}
