using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    // The session-centred window with two projects at once: one "new session" dialog picks agent and project together, the sidebar lists
    // sessions grouped by project with a status dot each, switching between them moves the window to that session's project, and sessions
    // of different projects can sit in the layout side by side. Plain CMD sessions stand in for the agents so nothing real is started.
    internal static class SessionsUiTest {
        internal static async Task Run(MainWindow window) {
            var json=new JavaScriptSerializer();
            string projectA=Path.Combine(TestArtifacts.Root,"proj-a"),projectB=Path.Combine(TestArtifacts.Root,"proj-b");
            Directory.CreateDirectory(projectA);Directory.CreateDirectory(projectB);
            await Stage(window,@"
await window.orbitUiTest.closeAll();
await window.orbitUiTest.setFolder("+json.Serialize(projectA)+@");await window.orbitUiTest.setFolder("+json.Serialize(projectB)+@");
document.querySelector('#new-session').click();await wait(()=>document.querySelector('#modal').open&&document.querySelector('#modal .agent-choice'),'the new session dialog');
");
            await window.CaptureForTest(TestArtifacts.PathFor("sessions-dialog.png"));
            await Stage(window,@"document.querySelector('#modal').close();");
            await Stage(window,@"
const A="+json.Serialize(projectA)+@",B="+json.Serialize(projectB)+@";
const S=()=>window.orbitUiTest.diagnostics().sessions;
await window.orbitUiTest.closeAll();
await window.orbitUiTest.setFolder(A);await window.orbitUiTest.setFolder(B); // both become known projects
document.querySelector('#sidebar-sessions-tab').click();
const newButton=document.querySelector('#new-session');check(newButton&&newButton.offsetParent!==null,'the sidebar has no visible New Session button');
check(document.querySelector('#sidebar-sessions-tab').getAttribute('aria-selected')==='true'&&!document.querySelector('#session-list-panel').hidden,'the session list is not the first sidebar view');
check(document.querySelector('#session-list').textContent.includes('아직 세션이 없어요'),'an empty session list should say how to start');
const openDialog=async()=>{document.querySelector('#new-session').click();await wait(()=>document.querySelector('#modal').open&&document.querySelector('#modal .agent-choice'),'the new session dialog');};
const start=async(projectName,profile,count)=>{
  await openDialog();
  const choice=Array.from(document.querySelectorAll('#modal .project-choice:not(.project-other)')).find(b=>b.textContent.includes(projectName));check(choice,'the dialog does not offer project '+projectName);
  choice.click();await pause(50);
  check(document.querySelector('#modal .project-choice.selected').textContent.includes(projectName),'the chosen project is not marked');
  document.querySelector('#modal .agent-choice[data-profile='+profile+']').click();
  await wait(()=>S().length===count&&S()[count-1].pid,'session '+count+' started');
};
// the dialog offers agents and shells, and never starts anything without a project choice
await openDialog();
check(document.querySelector('#modal .agent-choice[data-profile=claude]')&&document.querySelector('#modal .agent-choice[data-profile=codex]'),'Claude and Codex are not offered');
check(Array.from(document.querySelectorAll('#modal .project-choice')).some(b=>b.textContent.includes('proj-a'))&&Array.from(document.querySelectorAll('#modal .project-choice')).some(b=>b.textContent.includes('proj-b')),'the dialog does not list the known projects');
document.querySelector('#modal').close();
await start('proj-a','cmd',1);await start('proj-b','cmd',2);await start('proj-a','cmd',3);
const groups=()=>Array.from(document.querySelectorAll('#session-list .session-group')).map(g=>({project:g.querySelector('.session-group-head strong').textContent,rows:Array.from(g.querySelectorAll('.session-row .session-label')).map(x=>x.textContent)}));
const g=groups();
check(g.length===2&&g[0].project==='proj-a'&&g[1].project==='proj-b','sessions are not grouped by project: '+JSON.stringify(g));
check(JSON.stringify(g[0].rows)===JSON.stringify(['CMD','CMD 2'])&&JSON.stringify(g[1].rows)===JSON.stringify(['CMD']),'labels under a project are wrong: '+JSON.stringify(g));
const tabNames=Array.from(document.querySelectorAll('.terminal-tab .terminal-tab-name')).map(x=>x.textContent);
check(tabNames.includes('proj-a · CMD')&&tabNames.includes('proj-b · CMD')&&tabNames.includes('proj-a · CMD 2'),'tabs do not carry the project: '+JSON.stringify(tabNames));
const rowOf=id=>document.querySelector('.session-row[data-terminal-id=""'+id+'""]');
// switching moves the window to that session's project
rowOf(S()[1].id).querySelector('.session-row-open').click();await pause(100);
check(document.querySelector('#breadcrumb-folder').textContent==='proj-b','the header did not follow the session to project B: '+document.querySelector('#breadcrumb-folder').textContent);
check(document.querySelector('#status-folder').textContent.endsWith('proj-b'),'the status bar did not follow');
rowOf(S()[0].id).querySelector('.session-row-open').click();await pause(100);
check(document.querySelector('#breadcrumb-folder').textContent==='proj-a','the header did not follow the session back to project A');
check(rowOf(S()[0].id).classList.contains('active'),'the selected session row is not highlighted');
");
            await window.CaptureForTest(TestArtifacts.PathFor("sessions-list.png"));
            await Stage(window,@"
const S=()=>window.orbitUiTest.diagnostics().sessions;
const rowOf=id=>document.querySelector('.session-row[data-terminal-id=""'+id+'""]');
// status: put project B's session in front, let project A's session (out of view) produce output
rowOf(S()[1].id).querySelector('.session-row-open').click();await pause(100);
const hidden=S()[0].id;
const beforeCount=S()[0].outputCount;await post('write',{session:hidden,data:'echo STATUS_PROBE\r'});
const seen=[];for(let n=0;n<200&&seen.at(-1)!=='working';n++){seen.push(rowOf(hidden).dataset.status);await pause(30);}
check(seen.at(-1)==='working','a session producing output should read as working; saw '+JSON.stringify([...new Set(seen)])+', output '+beforeCount+' -> '+S()[0].outputCount+', echoed '+S()[0].output.includes('STATUS_PROBE'));
check(rowOf(hidden).querySelector('.session-status').textContent==='작업 중','the working label is wrong');
await wait(()=>rowOf(hidden).dataset.status==='attention','a hidden session that produced output and went quiet should ask for a look');
check(rowOf(hidden).querySelector('.session-status').textContent==='확인해 보세요','the attention label is wrong');
check(document.querySelector('.terminal-tab[data-terminal-id=""'+hidden+'""]').dataset.status==='attention','the tab does not show the attention status');
rowOf(hidden).querySelector('.session-row-open').click();
await wait(()=>rowOf(hidden).dataset.status==='idle','looking at the session should clear the mark');
// splitting still works, and a new tab in a group starts from the same dialog
document.querySelector('.tab-add').click();
await wait(()=>document.querySelector('#modal').open&&document.querySelector('#modal .agent-choice'),'the tab plus button did not open the session dialog');
document.querySelector('#modal').close();
// an ended session says so
await post('write',{session:S()[2].id,data:'exit\r'});
await wait(()=>rowOf(S()[2].id).dataset.status==='ended','an ended session should read as ended');
check(rowOf(S()[2].id).querySelector('.session-status').textContent==='끝남','the ended label is wrong');
");
            await Stage(window,@"
const A="+json.Serialize(projectA)+@";
const S=()=>window.orbitUiTest.diagnostics().sessions;
// the project Home is still there for managing projects: choosing a project takes you to its running session
document.querySelector('#home-return').click();
await wait(()=>document.querySelector('#app').classList.contains('home-active')&&document.querySelector('.home-project-dot'),'the project Home did not open');
const dot=Array.from(document.querySelectorAll('.home-project-dot')).find(d=>d.dataset.workspace===A);check(dot,'project A is not on the Home screen');
dot.click();
await wait(()=>!document.querySelector('#app').classList.contains('home-active'),'opening a project did not return to the workspace');
await pause(150);check(document.querySelector('#breadcrumb-folder').textContent==='proj-a','opening a project did not go to its running session');
check(!document.querySelector('#modal').open,'opening a project with a running session must not ask for a new one');
// the command palette opens from its top bar button and from Ctrl+K, and offers the session actions
document.querySelector('#command-button').click();
await wait(()=>document.querySelector('#modal').open&&document.querySelector('#modal .command-item'),'the command button did not open the command palette');
check(Array.from(document.querySelectorAll('#modal .command-item')).some(x=>x.textContent.includes('새 세션'))&&document.querySelectorAll('#modal .command-item').length>=8,'the command palette is missing its actions');
document.querySelector('#modal').close();
document.dispatchEvent(new KeyboardEvent('keydown',{key:'k',code:'KeyK',ctrlKey:true,bubbles:true}));
await wait(()=>document.querySelector('#modal').open&&document.querySelector('#modal .command-item'),'Ctrl+K did not open the command palette');
document.querySelector('#modal').close();
await window.orbitUiTest.closeAll();
");
            File.WriteAllText(TestArtifacts.PathFor("sessions-report.txt"),"PASS session-centred window: New Session dialog picks project and agent together, sessions grouped by project with per-project labels and tab names, the header follows the session in front, working/attention/idle/ended status for sessions in and out of view, the tab plus opens the same dialog, and Home still opens a project's running session.");
        }
        private static async Task Stage(MainWindow window,string body,int attempts=600) {
            string prefix=@"window.__orbitSessionsStage={done:false};(async()=>{try{
const pause=ms=>new Promise(r=>setTimeout(r,ms));
let nextTestId=-30000;
const post=(method,args)=>new Promise((resolve,reject)=>{const id=nextTestId--;const receive=({data})=>{if(data.id!==id)return;window.chrome.webview.removeEventListener('message',receive);data.error?reject(Error(data.error)):resolve(data.result);};window.chrome.webview.addEventListener('message',receive);window.chrome.webview.postMessage({id,method,args});});
const check=(value,label)=>{if(!value)throw Error(label);};
const wait=async(test,label)=>{for(let n=0;n<220;n++){if(test())return;await pause(30);}throw Error('Sessions UI timeout: '+label);};
";
            await window.ExecuteForTest(prefix+body+"window.__orbitSessionsStage={done:true};}catch(e){window.__orbitSessionsStage={done:true,error:String(e.stack||e)};}})();'started'");
            var json=new JavaScriptSerializer();
            for(int i=0;i<attempts;i++) {
                var raw=await window.ExecuteForTest("JSON.stringify(window.__orbitSessionsStage)");
                var state=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(json.Deserialize<string>(raw));
                if(state.ContainsKey("done")&&(bool)state["done"]){if(state.ContainsKey("error"))throw new InvalidOperationException((string)state["error"]);return;}
                await Task.Delay(100);
            }
            throw new TimeoutException("Sessions UI stage timed out");
        }
    }
}
