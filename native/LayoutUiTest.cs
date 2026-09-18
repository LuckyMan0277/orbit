using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.WinForms;

namespace Orbit {
    internal static class LayoutUiTest {
        internal static async Task Run(MainWindow window) {
            await window.ExecuteForTest(Script);
            string result=null;
            for(int i=0;i<450;i++) {
                result=await window.ExecuteForTest("JSON.stringify(window.__orbitLayoutTest || null)");
                if(result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)>=0)break;
                await Task.Delay(100);
            }
            File.WriteAllText(TestArtifacts.PathFor("terminal-layout-report.json"),result??"null");
            if(result==null || result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)<0)throw new TimeoutException("terminal layout UI test timed out");
            if(result.IndexOf("\\\"error\\\"",StringComparison.Ordinal)>=0)throw new InvalidOperationException(result);
            await VerifyPointerResize(window);
        }
        private static async Task VerifyPointerResize(MainWindow window) {
            var json=new JavaScriptSerializer();
            var before=json.Deserialize<Dictionary<string,object>>(await window.ExecuteForTest("(()=>{const s=document.querySelector('.terminal-splitter[aria-orientation=vertical]').getBoundingClientRect();return {x:s.x+s.width/2,y:s.y+s.height/2,width:document.querySelector('.terminal-group').clientWidth};})()"));
            double x=Convert.ToDouble(before["x"]),y=Convert.ToDouble(before["y"]);
            var web=(WebView2)typeof(MainWindow).GetField("web",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            foreach(var input in new [] {
                new {type="mouseMoved",x=x,y=y,button="none",buttons=0,clickCount=0},
                new {type="mousePressed",x=x,y=y,button="left",buttons=1,clickCount=1},
                new {type="mouseMoved",x=x+40,y=y,button="left",buttons=1,clickCount=0},
                new {type="mouseReleased",x=x+40,y=y,button="left",buttons=0,clickCount=1}
            }) {
                await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(input));
                await Task.Delay(60);
            }
            double after=Convert.ToDouble(await window.ExecuteForTest("document.querySelector('.terminal-group').clientWidth"));
            if(Math.Abs(after-Convert.ToDouble(before["width"]))<4)throw new InvalidOperationException("real mouse drag did not resize terminal panels");
            File.WriteAllText(TestArtifacts.PathFor("pointer-resize.txt"),"PASS Chromium mouse press/move/release resized the terminal split");
            var tab=json.Deserialize<Dictionary<string,object>>(await window.ExecuteForTest("(()=>{const r=document.querySelector('.terminal-tab-name').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()"));
            x=Convert.ToDouble(tab["x"]);y=Convert.ToDouble(tab["y"]);
            for(int count=1;count<=2;count++) {
                await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new {type="mousePressed",x=x,y=y,button="left",buttons=1,clickCount=count}));
                await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new {type="mouseReleased",x=x,y=y,button="left",buttons=0,clickCount=count}));
                await Task.Delay(60);
            }
            if((await window.ExecuteForTest("document.querySelector('#modal').open"))!="true")throw new InvalidOperationException("double-clicking a tab did not open rename");
            await window.ExecuteForTest("document.querySelector('#modal .dialog-input').value='검증 터미널';document.querySelector('#modal .primary').click();'renamed'");
            await Task.Delay(100);
            if((await window.ExecuteForTest("document.querySelector('.terminal-tab-name').textContent.includes('검증 터미널')"))!="true")throw new InvalidOperationException("renaming did not update the cached terminal tab label");
        }
        private const string Script=@"
window.__orbitLayoutTest={done:false};
(async()=>{try {
    const pause=ms=>new Promise(r=>setTimeout(r,ms));
    const check=(ok,message)=>{if(!ok)throw new Error(message);};
    const groups=()=>[...document.querySelectorAll('.terminal-group')];
    const tab=id=>[...document.querySelectorAll('.terminal-tab')].find(node=>node.dataset.terminalId===id);
    const group=id=>tab(id)?.closest('.terminal-group');
    const tabs=g=>[...g.querySelectorAll('.terminal-tab')].map(t=>t.dataset.terminalId);
    const rect=node=>{const r=node.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};};
    const select=async id=>{const name=tab(id).querySelector('.terminal-tab-name');name.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:1}));name.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,button:0,pointerId:1}));name.click();await pause(300);check(tab(id).classList.contains('active'),'click did not activate the requested tab');};
    const drag=async(id,target,fx=.5,fy=.5)=>{
        const source=tab(id),data=new DataTransfer(),start=rect(source),end=rect(target);
        source.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:1,clientX:start.x+8,clientY:start.y+10}));
        check(source.isConnected,'pointerdown detached the draggable tab');
        source.dispatchEvent(new DragEvent('dragstart',{bubbles:true,cancelable:true,dataTransfer:data}));
        const options={bubbles:true,cancelable:true,dataTransfer:data,clientX:end.x+end.width*fx,clientY:end.y+end.height*fy};
        target.dispatchEvent(new DragEvent('dragover',options));
        target.dispatchEvent(new DragEvent('drop',options));
        source.dispatchEvent(new DragEvent('dragend',{bubbles:true,dataTransfer:data}));
        await pause(140);
        const ids=[...document.querySelectorAll('.terminal-tab')].map(t=>t.dataset.terminalId);
        check(new Set(ids).size===ids.length,'a terminal appeared in more than one group');
        for(const g of groups()){
            const header=rect(g.querySelector('.terminal-group-tabs')),body=rect(g.querySelector('.terminal-group-body'));
            check(Math.abs(header.x-body.x)<2 && header.bottom<=body.y+2,'group tabs do not align with their terminal');
            check(g.querySelectorAll('.terminal-pane').length===1,'group does not show exactly one terminal');
        }
    };
    const body=id=>group(id).querySelector('.terminal-group-body');
    const before=window.orbitDiagnostics().sessions.map(s=>({id:s.id,pid:s.pid}));
    check(before.length===2 && groups().length===2,'initial split did not create two independent groups');
    const a=before[0].id,b=before[1].id;
    document.querySelector('#editor-tabs .tab-close').click();await pause(120);
    const positions=groups().map(g=>({id:g.dataset.groupId,...rect(g)}));
    await select(a);await select(b);
    check(JSON.stringify(positions)===JSON.stringify(groups().map(g=>({id:g.dataset.groupId,...rect(g)}))),'focus changed the layout');
    await window.orbitUiTest.startCmd();
    for(let i=0;i<100&&!window.orbitDiagnostics().sessions[2]?.pid;i++)await pause(50);
    const c=window.orbitDiagnostics().sessions[2].id;
    before.push({id:c,pid:window.orbitDiagnostics().sessions[2].pid});
    check(group(c)===group(b),'new terminal did not join the active group');
    await select(b);await select(c);
    await drag(c,tab(b),.05,.5);
    check(tabs(group(b))[0]===c,'tab reordering failed');
    for(const [edge,x,y] of [['top',.5,.05],['bottom',.5,.95],['left',.05,.5],['right',.95,.5]]){
        await drag(c,body(a),x,y);
        check(groups().length===3,'edge drop did not split: '+edge);
        const ar=rect(group(a)),cr=rect(group(c));
        check(edge==='top'?cr.bottom<=ar.y+2:edge==='bottom'?cr.y>=ar.bottom-2:edge==='left'?cr.right<=ar.x+2:cr.x>=ar.right-2,'wrong split orientation: '+edge);
        await drag(c,body(a));
        check(groups().length===2 && group(c)===group(a),'merge did not collapse the empty source group: '+edge);
    }
    await drag(c,body(a),.5,.95);
    const splitters=[...document.querySelectorAll('.terminal-splitter')];
    const divider=splitters.find(s=>s.getAttribute('aria-orientation')==='vertical');
    check(divider,'left/right splitter is missing');
    const widthBefore=rect(group(b)).width;
    divider.focus();divider.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true,cancelable:true}));
    await pause(160);
    check(Math.abs(rect(group(b)).width-widthBefore)>4,'splitter keyboard resize did not change panel width');
    await select(c);await window.orbitUiTest.send('echo ORBIT_LAYOUT_INPUT_OK\r\n');
    for(let i=0;i<120&&!window.orbitDiagnostics().sessions.find(s=>s.id===c).output.includes('ORBIT_LAYOUT_INPUT_OK');i++)await pause(50);
    check(window.orbitDiagnostics().sessions.find(s=>s.id===c).output.includes('ORBIT_LAYOUT_INPUT_OK'),'terminal input stopped after moves');
    for(const original of before)check(window.orbitDiagnostics().sessions.find(s=>s.id===original.id)?.pid===original.pid,'moving a tab restarted its process');
    check(window.orbitDiagnostics().sessions.find(s=>s.id===a).output.includes('80235'),'terminal output was lost during layout changes');
    const layout=groups().map(g=>({id:g.dataset.groupId,tabs:tabs(g),...rect(g)}));
    window.__orbitLayoutTest={done:true,checks:['group tab alignment','stable focus','tab reorder','four edge splits','group merge and collapse','keyboard resize','PID and output preservation','terminal input after moves'],layout};
}catch(e){window.__orbitLayoutTest={done:true,error:String(e&&e.stack||e)};}})();
'started';";
    }
}
