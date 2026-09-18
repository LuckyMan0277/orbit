using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    internal static class ThemeUiTest {
        internal static async Task Run(MainWindow window) {
            var json=new JavaScriptSerializer();
            string reference=Path.Combine(TestArtifacts.Root,"markdown-fixture","README.md");
            await window.ExecuteForTest(Script.Replace("__REFERENCE__",json.Serialize(reference)));
            string result=null;
            for(int i=0;i<200;i++) {
                result=await window.ExecuteForTest("JSON.stringify(window.__orbitThemeTest || null)");
                if(result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)>=0)break;
                await Task.Delay(100);
            }
            File.WriteAllText(TestArtifacts.PathFor("theme-report.json"),result??"null");
            if(result==null || result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)<0)throw new TimeoutException("Theme UI test timed out");
            if(result.IndexOf("\\\"error\\\"",StringComparison.Ordinal)>=0)throw new InvalidOperationException(result);
            var preferences=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(TestArtifacts.PathFor("webview-profile/settings.json")));
            if(!preferences.ContainsKey("theme") || (string)preferences["theme"]!="light")throw new InvalidOperationException("Light theme was not persisted");
            if(window.BackColor.GetBrightness()<0.5)throw new InvalidOperationException("Native window border remained dark in light mode");
            await window.CaptureForTest(TestArtifacts.PathFor("theme-light-desktop.png"));
            await window.ExecuteForTest("document.querySelector('#pick-folder').click();'opened'");
            for(int i=0;i<100;i++) {
                if((await window.ExecuteForTest("Boolean(document.querySelector('#file-picker')?.open && document.querySelector('#file-picker .file-picker-entry'))"))=="true")break;
                await Task.Delay(50);
            }
            await window.CaptureForTest(TestArtifacts.PathFor("theme-light-picker.png"));
            await window.ExecuteForTest("document.querySelector('#file-picker [data-picker-action=cancel]').click();document.querySelector('#preferences').click();'settings'");
            await Task.Delay(100);
            await window.ExecuteForTest("const themeSelect=document.querySelector('#theme-mode');themeSelect.value='dark';themeSelect.dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('#modal').close();'dark'");
            await Task.Delay(150);
            if((await window.ExecuteForTest("document.documentElement.dataset.theme==='dark'"))!="true")throw new InvalidOperationException("Dark theme did not restore");
        }
        private const string Script=@"
window.__orbitThemeTest={done:false};
(async()=>{try{
    const check=(ok,message)=>{if(!ok)throw new Error(message);};
    const pause=ms=>new Promise(r=>setTimeout(r,ms));
    const wait=async(test,label)=>{for(let i=0;i<150;i++){if(test())return;await pause(30);}throw new Error('Theme timed out: '+label);};
    const switchTheme=async value=>{document.querySelector('#preferences').click();await wait(()=>document.querySelector('#theme-mode'),'theme setting');const select=document.querySelector('#theme-mode');select.value=value;select.dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('#modal').close();await wait(()=>document.documentElement.dataset.theme===value,'theme applied');await pause(80);};
    const bright=node=>{const channels=getComputedStyle(node).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number);return channels.reduce((a,b)=>a+b)/3;};
    const edit=()=>document.querySelector('#editor-host .cm-editor');
    const draft=window.orbitUiTest.diagnostics().file.path;
    document.querySelector('[data-editor-mode=edit]').click();await pause(80);
    const content='# Theme preservation\n\nUnsaved work survives theme changes.\n';
    await window.orbitUiTest.editContent(content);
    const node=edit(),before=window.orbitDiagnostics().sessions.map(s=>({id:s.id,pid:s.pid,output:s.output}));
    await switchTheme('light');
    check(edit()===node,'theme change recreated the editor');
    check(bright(edit())>180,'editor stayed dark');
    check([...document.querySelectorAll('.xterm-viewport')].every(node=>bright(node)>180),'existing terminals stayed dark');
    check(window.orbitUiTest.diagnostics().file.content===content&&window.orbitUiTest.diagnostics().file.dirty,'theme change lost dirty text');
    await window.orbitUiTest.openFile(__REFERENCE__);
    await switchTheme('dark');
    await window.orbitUiTest.openFile(draft);
    check(bright(edit())<80,'cached file state restored its old light theme');
    check(window.orbitUiTest.diagnostics().file.content===content,'cached file lost edits');
    await switchTheme('light');
    const after=window.orbitDiagnostics().sessions;
    for(const session of before){const current=after.find(s=>s.id===session.id);check(current?.pid===session.pid,'theme restarted a terminal');check(current.output===session.output,'theme changed terminal output');}
    await window.orbitUiTest.save();
    await window.orbitUiTest.openFile(__REFERENCE__);
    await wait(()=>!document.querySelector('#markdown-preview').hidden,'Markdown preview');
    check(bright(document.querySelector('#markdown-preview'))>180,'Markdown stayed dark');
    window.__orbitThemeTest={done:true,checks:['live light/dark switch','same editor instance','dirty text preserved','cached file states rethemed','terminal process/output preserved','light Markdown','theme saved']};
}catch(e){window.__orbitThemeTest={done:true,error:String(e&&e.stack||e)};}})();
'started';";
    }
}
