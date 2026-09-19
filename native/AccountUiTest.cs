using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    internal static class AccountUiTest {
        internal static async Task Run(MainWindow window) {
            var json=new JavaScriptSerializer();
            await Stage(window,@"
await post('remoteStart',{port:48999});
await post('remotePublicOrigin',{url:'https://orbit-account-test.example'});
await pause(100);
document.querySelector('[aria-label=""기기 연결""]').click();
await wait(()=>document.querySelector('#modal').open,'dialog open');
await wait(()=>document.querySelector('.remote-account-section input[type=email]'),'account form');
check(!!find('계정 만들기')&&!!find('이 계정에 연결'),'account form exposes sign-up and link actions');
const advanced=document.querySelector('.remote-advanced');
check(advanced&&!advanced.open,'advanced is closed by default');
advanced.querySelector('summary').click();
await wait(()=>advanced.open,'advanced opens');
check(!!find('연결 상태 확인')&&!!find('외부 접속 끄기'),'advanced exposes troubleshooting and stop');
check(!!document.querySelector('.remote-advanced input[placeholder^=""계정 서비스""]'),'advanced exposes the account service url field');
advanced.open=false;
document.querySelector('.remote-account-section input[type=email]').value='test@example.com';
document.querySelector('.remote-account-section input[type=password]').value='irrelevant-password';
find('계정 만들기').click();
await wait(()=>document.querySelector('.remote-account-section .dialog-note')?.textContent,'sign-up error rendered');
check(document.querySelector('.remote-account-section .dialog-note').textContent.includes('계정 서비스 주소'),'sign-up without a configured service url fails clearly');
document.documentElement.dataset.theme='dark';
await pause(60);
");
            string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            await window.ExecuteForTest("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
            await window.CaptureForTest(TestArtifacts.PathFor("remote-account-dark-"+stamp+".png"));
            await window.ExecuteForTest("document.documentElement.dataset.theme='light';'ok'");
            await Task.Delay(300);
            await window.CaptureForTest(TestArtifacts.PathFor("remote-account-light-"+stamp+".png"));
            await Stage(window,@"await post('remoteStop',{});document.querySelector('#modal').close();");
            File.WriteAllText(TestArtifacts.PathFor("remote-account-report.txt"),"PASS actual remote modal, default-closed advanced controls, account sign-up/link form, account service url field, and a clear error when the service url is unset. No external requests.");
        }
        private static async Task Stage(MainWindow window,string body,int attempts=100) {
            string prefix=@"window.__orbitAccountStage={done:false};(async()=>{try{
const pause=ms=>new Promise(r=>setTimeout(r,ms));
let nextTestId=-10000;
const post=(method,args)=>new Promise((resolve,reject)=>{const id=nextTestId--;const receive=({data})=>{if(data.id!==id)return;window.chrome.webview.removeEventListener('message',receive);data.error?reject(Error(data.error)):resolve(data.result);};window.chrome.webview.addEventListener('message',receive);window.chrome.webview.postMessage({id,method,args});});
const check=(value,label)=>{if(!value)throw Error(label);};
const find=text=>Array.from(document.querySelectorAll('#modal button')).find(b=>b.textContent===text);
const wait=async(test,label)=>{for(let n=0;n<150;n++){if(test())return;await pause(30);}throw Error('Account UI timeout: '+label);};
";
            await window.ExecuteForTest(prefix+body+"window.__orbitAccountStage={done:true};}catch(e){window.__orbitAccountStage={done:true,error:String(e.stack||e)};}})();'started'");
            var json=new JavaScriptSerializer();
            for(int i=0;i<attempts;i++) {
                var raw=await window.ExecuteForTest("JSON.stringify(window.__orbitAccountStage)");
                var state=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(json.Deserialize<string>(raw));
                if(state.ContainsKey("done")&&(bool)state["done"]){if(state.ContainsKey("error"))throw new InvalidOperationException((string)state["error"]);return;}
                await Task.Delay(100);
            }
            throw new TimeoutException("Account UI stage timed out");
        }
    }
}
