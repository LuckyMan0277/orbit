using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    // The API key vault against real shells, with two projects open at the same time: keys are stored per project (or for all projects),
    // every terminal keeps the project it was opened in whatever the window shows, and an AI's request says which project and terminal asked.
    internal static class SecretsUiTest {
        const string Key="DESK_KEY",Shared="DESK_SHARED",Asked="DESK_ASKED",ValueA="sk-ant-desk-a-value-42",ValueB="desk-b-value-77",SharedValue="desk-shared-value-11",AskedValue="asked-value-99";
        internal static async Task Run(MainWindow window) {
            var json=new JavaScriptSerializer();
            string projectA=Path.Combine(TestArtifacts.Root,"proj-a"),projectB=Path.Combine(TestArtifacts.Root,"proj-b");
            Directory.CreateDirectory(projectA);Directory.CreateDirectory(projectB);
            bool tool=File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bin","orbit-secret.exe"));
            await window.CaptureForTest(TestArtifacts.PathFor("secrets-home.png"));
            string setup=@"
const A="+json.Serialize(projectA)+@",B="+json.Serialize(projectB)+@";
for(const project of [A,B,'']){for(const name of ['"+Key+@"','"+Asked+@"','"+Shared+@"']){await post('secretDelete',{name,scope:'project',project});await post('secretDelete',{name,scope:'global',project});}}
await window.orbitUiTest.closeAll();
const S=()=>window.orbitUiTest.diagnostics().sessions;
const openKeys=async()=>{const b=Array.from(document.querySelectorAll('.secrets-open')).find(x=>x.offsetParent!==null);check(b&&b.getAttribute('aria-label')==='API 키 보관함','no visible API key button in the header');b.click();await wait(()=>document.querySelector('#modal .secret-list')&&(!document.querySelector('#modal .secret-form').hidden||!document.querySelector('#modal .secret-add').hidden),'keys dialog loaded');};
const rows=()=>Array.from(document.querySelectorAll('#modal .secret-row code')).map(x=>x.textContent);
const addKey=async(value,name,scope)=>{
  await openKeys();const add=document.querySelector('#modal .secret-add');if(!add.hidden)add.click();
  const [valueInput,nameInput]=document.querySelectorAll('#modal .secret-form input');check(valueInput.type==='password','key value is not typed into a password field');
  valueInput.value=value;valueInput.dispatchEvent(new Event('input'));
  if(value.startsWith('sk-ant-'))check(nameInput.value==='ANTHROPIC_API_KEY','pasting an Anthropic key did not suggest its name');
  nameInput.value=name;nameInput.dispatchEvent(new Event('input'));
  const select=document.querySelector('#modal .secret-scope');check(select&&!select.hidden&&select.value==='project','with a project open the scope must default to that project');
  select.value=scope;find('저장').click();
  await wait(()=>rows().includes(name),'saved key listed');check(valueInput.value==='','value field was not cleared after saving');
  check(!document.querySelector('#modal').textContent.includes(value),'key value is shown in the dialog');
  document.querySelector('#modal').close();
};
// project A: open a terminal, save a project key
await window.orbitUiTest.setFolder(A);await window.orbitUiTest.startCmd();
await wait(()=>S().length===1&&S()[0].pid,'terminal of project A');
await addKey('"+ValueA+@"','"+Key+@"','project');
// project B: A's project key must not be visible here; save B's own value of the same name and a key for all projects
await window.orbitUiTest.setFolder(B);
await openKeys();check(!rows().includes('"+Key+@"'),'a key of project A is listed while project B is open');document.querySelector('#modal').close();
await addKey('"+SharedValue+@"','"+Shared+@"','global');
await addKey('"+ValueB+@"','"+Key+@"','project');
await window.orbitUiTest.startCmd();await wait(()=>S().length===2&&S()[1].pid,'terminal of project B');
await wait(()=>{const b=document.querySelector('.secrets-count');return b&&!b.hidden&&b.textContent==='2';},'header badge counts what project B can use');
";
            await Stage(window,setup);
            await window.CaptureForTest(TestArtifacts.PathFor("secrets-workspace.png"));
            await Stage(window,@"const b=Array.from(document.querySelectorAll('.secrets-open')).find(x=>x.offsetParent!==null);b.click();await wait(()=>document.querySelectorAll('#modal .secret-row').length===2,'keys listed');");
            await window.CaptureForTest(TestArtifacts.PathFor("secrets-dialog.png"));
            await Stage(window,@"document.querySelector('#modal').close();");
            string file=TestArtifacts.PathFor("webview-profile/secrets.dat");
            if(!File.Exists(file)) throw new InvalidOperationException("secret vault file was not written");
            byte[] disk=File.ReadAllBytes(file);
            foreach(string secret in new[]{ValueA,ValueB,SharedValue}) if(Encoding.UTF8.GetString(disk).Contains(secret)||Encoding.Unicode.GetString(disk).Contains(secret)) throw new InvalidOperationException("secret is stored in plain text");
            if(tool) {
            await Stage(window,@"
const S=()=>window.orbitUiTest.diagnostics().sessions;
const flat=i=>S()[i].output.replace(/\n/g,''); // a typed line wraps in a narrow pane
const send=async(i,text)=>{await post('write',{session:S()[i].id,data:text+'\r'});};
// a plain shell gets no keys on its own, and reaches them through orbit-secret run, each with its own project's value
await send(0,'echo [%"+Key+@"%]PLAIN');await send(1,'echo [%"+Key+@"%]PLAIN');
await wait(()=>flat(0).split('[%"+Key+@"%]PLAIN').length>=3&&flat(1).split('[%"+Key+@"%]PLAIN').length>=3,'plain shells answered without keys');
await send(0,'orbit-secret run -- cmd /c echo [%"+Key+@"%][%"+Shared+@"%]DONE');await send(1,'orbit-secret run -- cmd /c echo [%"+Key+@"%][%"+Shared+@"%]DONE');
await wait(()=>flat(0).includes('["+ValueA+@"]["+SharedValue+@"]DONE')&&flat(1).includes('["+ValueB+@"]["+SharedValue+@"]DONE'),'each terminal must get its own project key plus the shared one, although the window shows project B: '+JSON.stringify([flat(0).slice(-160),flat(1).slice(-160)]));
check(!flat(1).includes('["+ValueA+@"]')&&!flat(0).includes('["+ValueB+@"]'),'a project key reached the other project terminal');
await send(0,'orbit-secret list');await wait(()=>flat(0).includes('this project only'),'orbit-secret list does not tag project-only keys');
// project A's terminal asks for a key while the window shows project B: the card names project A, and the key is stored for project A
await send(0,'orbit-secret request "+Asked+@" needed for the payments test');
await wait(()=>document.querySelector('#secret-asks .secret-ask')&&!document.querySelector('#secret-asks').hidden,'the request card did not appear');
const card=document.querySelector('#secret-asks .secret-ask');
check(card.textContent.includes('"+Asked+@"')&&card.textContent.includes('proj-a'),'the card does not say which project asked: '+card.textContent);
check(document.querySelector('.terminal-tab.asks-key'),'no tab is marked as waiting for a key');
check(!document.querySelector('#modal').open,'the request took the screen over instead of waiting');
");
            await window.CaptureForTest(TestArtifacts.PathFor("secrets-request-card.png"));
            await Stage(window,@"
const S=()=>window.orbitUiTest.diagnostics().sessions;
const flat=i=>S()[i].output.replace(/
/g,'');
const send=async(i,text)=>{await post('write',{session:S()[i].id,data:text+''});};
const card=document.querySelector('#secret-asks .secret-ask');
Array.from(card.querySelectorAll('button')).find(b=>b.textContent==='입력').click();
await wait(()=>document.querySelector('#modal').open&&document.querySelector('#modal input[type=password]'),'the input dialog did not open');
const scope=document.querySelector('#modal .secret-scope');check(scope&&scope.value==='project'&&scope.options[0].text.includes('proj-a'),'the scope must default to the project that asked');
document.querySelector('#modal input[type=password]').value='"+AskedValue+@"';find('저장').click();
await wait(()=>flat(0).includes('Saved. "+Asked+@"'),'request result was not printed to the terminal');
check(!flat(0).includes('"+AskedValue+@"'),'the value typed into the dialog appeared in the terminal');
check(document.querySelector('#secret-asks').hidden,'the card stayed after answering');
await send(0,'orbit-secret run -- cmd /c echo [%"+Asked+@"%]GOT');await send(1,'orbit-secret run -- cmd /c echo [%"+Asked+@"%]GOT');
await wait(()=>flat(0).includes('["+AskedValue+@"]GOT')&&flat(1).split('[%"+Asked+@"%]GOT').length>=3,'the key asked by project A must reach A only');
check(!flat(1).includes('["+AskedValue+@"]'),'project B received the key that project A asked for');
// refusing
await send(1,'orbit-secret request DESK_REFUSED');
await wait(()=>document.querySelector('#secret-asks .secret-ask'),'second card');
Array.from(document.querySelectorAll('#secret-asks .secret-ask button')).find(b=>b.textContent==='거절').click();
await wait(()=>flat(1).includes('declined to provide DESK_REFUSED'),'refusal was not reported to the terminal');
");
            }
            await Stage(window,@"
const A="+json.Serialize(projectA)+@",B="+json.Serialize(projectB)+@";
for(const project of [A,B]){for(const name of ['"+Key+@"','"+Asked+@"']){await post('secretDelete',{name,scope:'project',project});}}
await post('secretDelete',{name:'"+Shared+@"',scope:'global',project:B});
const left=await post('secretList',{project:A});check(!left.keys.some(k=>k.name==='"+Key+@"'||k.name==='"+Asked+@"'||k.name==='"+Shared+@"'),'keys were not deleted');
await window.orbitUiTest.closeAll();
");
            File.WriteAllText(TestArtifacts.PathFor("secrets-report.txt"),"PASS desktop API keys with two projects open at once: per-project and all-project keys, a project's keys never reach the other project, name suggestion from a pasted key, encrypted storage, and "+(tool?"orbit-secret run/list/request in real shells with the request card naming the project and terminal and storing the key for the terminal's own project.":"(orbit-secret is not part of this build, so the in-terminal tool was not exercised)."));
        }
        private static async Task Stage(MainWindow window,string body,int attempts=600) {
            string prefix=@"window.__orbitSecretsStage={done:false};(async()=>{try{
const pause=ms=>new Promise(r=>setTimeout(r,ms));
let nextTestId=-20000;
const post=(method,args)=>new Promise((resolve,reject)=>{const id=nextTestId--;const receive=({data})=>{if(data.id!==id)return;window.chrome.webview.removeEventListener('message',receive);data.error?reject(Error(data.error)):resolve(data.result);};window.chrome.webview.addEventListener('message',receive);window.chrome.webview.postMessage({id,method,args});});
const check=(value,label)=>{if(!value)throw Error(label);};
const find=text=>Array.from(document.querySelectorAll('#modal button')).find(b=>b.textContent===text);
const wait=async(test,label)=>{for(let n=0;n<200;n++){if(test())return;await pause(30);}throw Error('Secrets UI timeout: '+label);};
";
            await window.ExecuteForTest(prefix+body+"window.__orbitSecretsStage={done:true};}catch(e){window.__orbitSecretsStage={done:true,error:String(e.stack||e)};}})();'started'");
            var json=new JavaScriptSerializer();
            for(int i=0;i<attempts;i++) {
                var raw=await window.ExecuteForTest("JSON.stringify(window.__orbitSecretsStage)");
                var state=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(json.Deserialize<string>(raw));
                if(state.ContainsKey("done")&&(bool)state["done"]){if(state.ContainsKey("error"))throw new InvalidOperationException((string)state["error"]);return;}
                await Task.Delay(100);
            }
            throw new TimeoutException("Secrets UI stage timed out");
        }
    }
}
