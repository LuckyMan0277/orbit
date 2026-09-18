using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    internal static class PickerUiTest {
        internal static async Task Run(MainWindow window) {
            string root=TestArtifacts.Root;
            string folder=Path.Combine(root,"picker-fixture","한글 폴더");
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder,"하위 폴더"));
            string file=Path.Combine(folder,"검증 노트.md");
            string existing=Path.Combine(folder,"existing.txt");
            string created=Path.Combine(folder,"created-"+Guid.NewGuid().ToString("N")+".md");
            File.WriteAllText(file,"# Picker test\n\nUnicode paths work.\n",new UTF8Encoding(false));
            File.WriteAllText(existing,"Keep existing contents.",new UTF8Encoding(false));
            var json=new JavaScriptSerializer();
            await window.ExecuteForTest(Script.Replace("__ROOT__",json.Serialize(root)).Replace("__FOLDER__",json.Serialize(folder)).Replace("__FILE__",json.Serialize(file)).Replace("__NEW_NAME__",json.Serialize(Path.GetFileName(created))));
            string result=null;
            for(int i=0;i<300;i++) {
                result=await window.ExecuteForTest("JSON.stringify(window.__orbitPickerTest || null)");
                if(result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)>=0)break;
                await Task.Delay(100);
            }
            File.WriteAllText(TestArtifacts.PathFor("picker-report.json"),result??"null");
            if(result==null || result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)<0)throw new TimeoutException("Picker UI test timed out");
            if(result.IndexOf("\\\"error\\\"",StringComparison.Ordinal)>=0)throw new InvalidOperationException(result);
            if(File.ReadAllText(existing)!="Keep existing contents.")throw new InvalidOperationException("Creating a file overwrote an existing file");
            if(!File.Exists(created) || new FileInfo(created).Length!=0)throw new InvalidOperationException("New file was not created through the native bridge");
            await window.CaptureForTest(TestArtifacts.PathFor("picker-desktop.png"));
            var normalSize=window.Size;
            window.Size=new System.Drawing.Size(900,620);
            await Task.Delay(100);
            await window.CaptureForTest(TestArtifacts.PathFor("picker-minimum.png"));
            window.Size=normalSize;
            await window.ExecuteForTest("document.querySelector('#file-picker [data-picker-action=cancel]').click();'closed'");
        }
        private const string Script=@"
window.__orbitPickerTest={done:false};
(async()=>{try{
    const check=(ok,message)=>{if(!ok)throw new Error(message);};
    const pause=ms=>new Promise(r=>setTimeout(r,ms));
    const wait=async(test,label)=>{for(let i=0;i<200;i++){if(test())return;await pause(30);}throw new Error('Picker timed out: '+label);};
    const picker=()=>document.querySelector('#file-picker');
    const input=()=>picker()?.querySelector('.file-picker-path');
    const confirm=()=>picker()?.querySelector('[data-picker-action=confirm]');
    const rows=()=>[...picker().querySelectorAll('.file-picker-entry')];
    const root=__ROOT__,folder=__FOLDER__,file=__FILE__,name=__NEW_NAME__;
    const ready=()=>picker()?.open&&input()&&!confirm().disabled;
    const open=async selector=>{document.querySelector(selector).click();await wait(()=>picker()?.open&&input(),'modal opened');await pause(100);};
    const navigate=async path=>{input().value=path;input().dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',bubbles:true}));await wait(()=>input()?.value.toLowerCase()===path.toLowerCase()&&rows().some(row=>row.dataset.path.toLowerCase().startsWith(path.toLowerCase()+'\\')),'directory loaded');await pause(80);};
    const cancel=async()=>{picker().querySelector('[data-picker-action=cancel]').click();await wait(()=>!picker()?.open,'cancel');};
    await open('#pick-folder');
    await navigate(folder);
    check(rows().every(row=>row.dataset.path!==file),'workspace picker included a file');
    input().value=folder+'\\missing-directory';input().dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',bubbles:true}));
    await wait(()=>picker().querySelector('.file-picker-error')?.textContent.trim(),'missing path error');
    await cancel();
    check(document.querySelector('#status-folder').textContent===root,'cancel changed workspace');
    await open('#pick-folder');
    input().value=folder;input().dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',bubbles:true}));
    await wait(()=>ready()&&input().value===folder,'empty directory ready');
    confirm().click();await wait(()=>document.querySelector('#status-folder').textContent===folder&&!picker()?.open,'workspace selected');
    await open('#quick-tools [aria-label=""파일 열기""]');
    await wait(()=>rows().some(row=>row.dataset.path===file),'file list');
    const filter=picker().querySelector('.file-picker-filter input');filter.value='검증';filter.dispatchEvent(new Event('input',{bubbles:true}));
    await wait(()=>rows().filter(row=>!row.hidden).some(row=>row.dataset.path===file),'name filter');
    const chosen=rows().find(row=>row.dataset.path===file);chosen.click();
    check(chosen.isConnected,'selection recreated the row and breaks real double-click');
    chosen.dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));
    await wait(()=>document.querySelector('#editor-path').textContent===file&&!picker()?.open,'file opened');
    await open('#tree-actions button[title=""새 파일""]');
    const filename=picker().querySelector('.file-picker-name');
    check(filename,'new file name field missing');
    filename.value='existing.txt';filename.dispatchEvent(new Event('input',{bubbles:true}));confirm().click();
    await wait(()=>picker()?.open&&picker().querySelector('.file-picker-error')?.textContent.trim(),'overwrite rejected');
    filename.value=name;filename.dispatchEvent(new Event('input',{bubbles:true}));confirm().click();
    await wait(()=>!picker()?.open&&document.querySelector('#editor-path').textContent.endsWith(name),'file created and opened');
    await open('#pick-folder');await navigate(root);confirm().click();
    await wait(()=>!picker()?.open&&document.querySelector('#status-folder').textContent===root,'workspace restored');
    await open('#quick-tools [aria-label=""파일 열기""]');await navigate(folder);
    window.__orbitPickerTest={done:true,checks:['themed folder/file/create dialogs','unicode and spaces','missing path error','cancel preserves workspace','file selection and filter','exclusive file creation','workspace restoration']};
}catch(e){window.__orbitPickerTest={done:true,error:String(e&&e.stack||e)};}})();
'started';";
    }
}
