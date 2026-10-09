window.__recoveryResult = { done: false };
(async () => {
  const timer = window.setTimeout.bind(window);
  const pause = ms => new Promise(resolve => timer(resolve, ms));
  const state = () => window.orbitDiagnostics().sessions[0];
  const rows = () => state().output.split('\n').map(line => line.trim());
  const has = marker => rows().includes(marker);
  const waitFor = async (predicate, label) => { for (let i=0;i<160;i++) {if(predicate())return;await pause(50);}throw Error(label); };
  const host = window.chrome.webview, send = host.postMessage.bind(host);
  const input = text => send({ id:0, method:'write', args:{session:state().id,data:text+'\r'} });
  const key = () => document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,cancelable:true}));
  const type = text => {for(const char of text){const n=char.charCodeAt(0);document.activeElement.dispatchEvent(new KeyboardEvent('keypress',{key:char,keyCode:n,charCode:n,which:n,bubbles:true,cancelable:true}));}key();};
  const assert = (value,label) => {if(!value)throw Error(label);};
  const initialPid = state().pid, packets = [];
  const listener = ({data}) => {if(data.type==='output'&&data.session===state().id)packets.push(data);};
  host.addEventListener('message',listener);
  try {
    assert(document.activeElement.classList.contains('xterm-helper-textarea'),'active tab click did not focus terminal input');
    type('echo RECOVERY_TAB_INPUT');await waitFor(()=>has('RECOVERY_TAB_INPUT'),'active-tab command did not execute');
    await window.orbitUiTest.openFile(__FIXTURE__);
    assert(document.activeElement.closest('#editor-host'),'editor did not receive focus');
    input('echo RECOVERY_EDITOR_BACKGROUND');await waitFor(()=>has('RECOVERY_EDITOR_BACKGROUND'),'background output did not render');
    assert(document.activeElement.closest('#editor-host'),'output handling stole editor focus');
    document.querySelector('.editor-tab.active .tab-close').click();await pause(200);
    assert(document.activeElement.classList.contains('xterm-helper-textarea'),'closing final editor did not restore terminal focus');

    let dropped=0;
    host.postMessage=message=>{if(message.method==='ack'&&!dropped){dropped++;return;}send(message);};
    input('echo RECOVERY_ACK_ONE');
    await waitFor(()=>dropped===1,'ACK injection did not occur');
    input('echo RECOVERY_ACK_TWO');
    await waitFor(()=>has('RECOVERY_ACK_TWO'),'one lost ACK did not automatically recover');
    host.postMessage=send;
    assert(rows().filter(x=>x==='RECOVERY_ACK_ONE').length===1,'lost ACK duplicated output');
    assert(rows().filter(x=>x==='RECOVERY_ACK_TWO').length===1,'recovered command missing/duplicated');
    const retriedSequence=packets.find((p,i)=>packets.slice(0,i).some(old=>old.seq===p.seq))?.seq;
    assert(retriedSequence,'native did not resend same sequence');

    // Pause xterm's parser timer while WebView messages remain responsive. A
    // pending duplicate must neither write again nor ACK ahead of the parser.
    const held=[];
    window.setTimeout=(fn,ms,...args)=>{if(!ms){held.push(()=>fn(...args));return 0;}return timer(fn,ms,...args);};
    const before=state().outputCount;
    input('echo RECOVERY_PARSER_PAUSE');
    await waitFor(()=>held.length>0,'xterm parser pause was not reached');
    input('echo RECOVERY_AFTER_PAUSE');
    await pause(3200);
    assert(!has('RECOVERY_PARSER_PAUSE')&&!has('RECOVERY_AFTER_PAUSE'),'pending parser was acknowledged early');
    assert(held.length===1,'pending retries queued another parser write');
    assert(state().outputCount>before,'paused renderer did not receive a chunk');
    window.setTimeout=timer;held.forEach(run=>run());
    await waitFor(()=>has('RECOVERY_AFTER_PAUSE'),'parser recovery did not resume output');
    assert(rows().filter(x=>x==='RECOVERY_PARSER_PAUSE').length===1,'paused parser duplicated output');

    input('for /l %i in (1,1,40) do @echo UI_RECOVERY_ROW_%i');
    input('echo RECOVERY_FLOOD_COMPLETE');
    await waitFor(()=>has('RECOVERY_FLOOD_COMPLETE'),'UI flood did not finish');
    const flood=rows().filter(x=>/^UI_RECOVERY_ROW_\d+$/.test(x));
    assert(flood.length===40&&flood.every((x,i)=>x==='UI_RECOVERY_ROW_'+(i+1)),'UI flood changed output order/count');
    assert(state().pid===initialPid&&!state().exited,'recovery replaced or exited terminal');
    const verified={pid:initialPid,activeTabInput:true,editorFocusPreserved:true,lastEditorCloseFocus:true,lostAckRecovered:true,sameSequenceRetried:retriedSequence,parserPauseMs:3200,pendingWrites:held.length,exactOrderedRows:flood.length};
    await window.orbitUiTest.closeAll();
    assert(window.orbitDiagnostics().sessions.length===0,'closed terminal remained in UI');
    window.__recoveryResult={done:true,ok:true,...verified};
  }catch(error){window.__recoveryResult={done:true,ok:false,error:String(error)};}
  finally{host.postMessage=send;window.setTimeout=timer;host.removeEventListener('message',listener);}
})();
