using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    internal static class QrUiTest {
        internal static async Task Run(MainWindow window) {
            var json=new JavaScriptSerializer();
            await Stage(window,@"
await post('remoteStart',{port:48999});
await post('remotePublicOrigin',{url:'https://orbit-qr-test.example'});
await pause(100);
document.querySelector('[aria-label=""기기 연결""]').click();
await wait(()=>document.querySelector('#modal').open,'dialog open');
await wait(()=>document.querySelector('#remote-connect-start'),'connection button');
check(getComputedStyle(document.querySelector('.remote-qr')).display==='none','empty QR must be hidden');
document.querySelector('#remote-connect-start').click();
await wait(()=>document.querySelector('.remote-qr img')?.complete,'QR visible');
check(!find('링크 복사').disabled,'copy must be enabled');
const advanced=document.querySelector('.remote-dialog-side details');
check(advanced&&!advanced.open,'advanced is closed by default');
advanced.querySelector('summary').click();
await wait(()=>advanced.open,'advanced opens');
check(!!find('연결 상태 확인')&&!!find('외부 접속 끄기'),'advanced exposes troubleshooting and stop');
advanced.open=false;
const box=document.querySelector('.remote-qr img').getBoundingClientRect();
check(box.width>=232&&box.width<=260&&Math.abs(box.width-box.height)<1,'QR size');
const link=document.querySelector('#modal input[readonly]').value;
check(link.startsWith('https://orbit-qr-test.example/mobile.html#pair='),'QR link');
document.documentElement.dataset.theme='dark';
await pause(60);
");
            File.WriteAllText(TestArtifacts.PathFor("remote-qr.svg"),json.Deserialize<string>(await window.ExecuteForTest("document.querySelector('.remote-qr').dataset.svg")));
            File.WriteAllText(TestArtifacts.PathFor("remote-qr-link.txt"),json.Deserialize<string>(await window.ExecuteForTest("document.querySelector('#modal input[readonly]').value")));
            string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            await window.ExecuteForTest("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
            await window.CaptureForTest(TestArtifacts.PathFor("remote-qr-dark-final-"+stamp+".png"));
            await window.ExecuteForTest("document.documentElement.dataset.theme='light';'ok'");
            await Task.Delay(300);
            await Stage(window,@"
const prior=document.querySelector('.remote-qr img').src;
document.querySelector('#remote-connect-start').click();
await wait(()=>document.querySelector('.remote-qr img')?.src!==prior,'light QR repaint');
");
            await window.ExecuteForTest("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
            await window.CaptureForTest(TestArtifacts.PathFor("remote-qr-light-final-"+stamp+".png"));
            await Stage(window,@"
const originalTimeout=window.setTimeout;
try {
window.setTimeout=(fn,delay,...args)=>originalTimeout(fn,delay===300000?350:delay,...args);
const previousLink=document.querySelector('#modal input[readonly]').value;
document.querySelector('#remote-connect-start').click();
await wait(()=>document.querySelector('.remote-qr img')&&document.querySelector('#modal input[readonly]').value!==previousLink&&!document.querySelector('#remote-connect-start').disabled,'renewed QR visible');
await wait(()=>!document.querySelector('.remote-qr img'),'expired QR clears');
check(find('링크 복사').disabled,'expired link unavailable');
} finally { window.setTimeout=originalTimeout; }
await post('remotePublicOrigin',{url:'https://orbit-qr-next.example'});
await wait(()=>!document.querySelector('.remote-qr img'),'old QR cleared');
check(document.querySelector('#modal input[readonly]').value==='','old link cleared');
check(find('링크 복사').disabled,'copy disabled after clear');
document.querySelector('#remote-connect-start').click();
await wait(()=>document.querySelector('.remote-qr img'),'new QR visible');
check(document.querySelector('#modal input[readonly]').value.startsWith('https://orbit-qr-next.example/'),'new origin link');
await post('remotePublicOrigin',{url:''});
await wait(()=>!document.querySelector('.remote-qr img'),'loopback clears QR');
check(document.querySelector('#remote-connect-start').textContent.indexOf('외부 주소 준비')>=0,'loopback returns to start action');
check(getComputedStyle(document.querySelector('.remote-qr')).display==='none','cleared QR hidden');
await post('remoteStop',{});
document.querySelector('#modal').close();
document.documentElement.dataset.theme='dark';
");
            File.WriteAllText(TestArtifacts.PathFor("remote-qr-report.txt"),"PASS actual remote modal, default-closed advanced controls, HTTPS QR, copy state, expiry with accelerated test timer, origin invalidation, loopback guard, dark/light captures. No external requests.");
        }
        // Opt-in because this starts and then stops the user's real Funnel.
        // It uses the normal WebView message path and does not approve a device.
        internal static async Task RunLive(MainWindow window) {
            await Stage(window,@"
await post('remoteStart',{port:49931});
document.querySelector('#external-connection').click();
await wait(()=>document.querySelector('#modal').open,'live dialog open');
await wait(()=>document.querySelector('#remote-connect-start'),'live connection button');
document.querySelector('#remote-connect-start').click();
let liveReady=false;for(let n=0;n<500;n++){const link=document.querySelector('#modal input[readonly]').value;if(document.querySelector('.remote-qr img')&&/^https:\/\/[a-z0-9.-]+\.ts\.net\/mobile\.html#pair=/.test(link)&&!document.querySelector('#remote-connect-start').disabled){liveReady=true;break;}await pause(30);}check(liveReady,'live Funnel QR: '+document.querySelector('#modal').innerText);
",180);
            await window.CaptureForTest(TestArtifacts.PathFor("remote-qr-live-funnel.png"));
            await Stage(window,@"await post('remoteStop',{});document.querySelector('#modal').close();");
            File.WriteAllText(TestArtifacts.PathFor("remote-qr-live-funnel-report.txt"),"PASS actual connection button produced a ts.net QR through the installed Tailscale Funnel, then stopped it. No device was approved.");
        }
        private static async Task Stage(MainWindow window,string body,int attempts=100) {
            string prefix=@"window.__orbitQrStage={done:false};(async()=>{try{
const pause=ms=>new Promise(r=>setTimeout(r,ms));
let nextTestId=-10000;
const post=(method,args)=>new Promise((resolve,reject)=>{const id=nextTestId--;const receive=({data})=>{if(data.id!==id)return;window.chrome.webview.removeEventListener('message',receive);data.error?reject(Error(data.error)):resolve(data.result);};window.chrome.webview.addEventListener('message',receive);window.chrome.webview.postMessage({id,method,args});});
const check=(value,label)=>{if(!value)throw Error(label);};
const find=text=>Array.from(document.querySelectorAll('#modal button')).find(b=>b.textContent===text);
const wait=async(test,label)=>{for(let n=0;n<150;n++){if(test())return;await pause(30);}throw Error('QR timeout: '+label);};
";
            await window.ExecuteForTest(prefix+body+"window.__orbitQrStage={done:true};}catch(e){window.__orbitQrStage={done:true,error:String(e.stack||e)};}})();'started'");
            var json=new JavaScriptSerializer();
            for(int i=0;i<attempts;i++) {
                var raw=await window.ExecuteForTest("JSON.stringify(window.__orbitQrStage)");
                var state=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(json.Deserialize<string>(raw));
                if(state.ContainsKey("done")&&(bool)state["done"]){if(state.ContainsKey("error"))throw new InvalidOperationException((string)state["error"]);return;}
                await Task.Delay(100);
            }
            throw new TimeoutException("QR UI stage timed out");
        }
    }
}
