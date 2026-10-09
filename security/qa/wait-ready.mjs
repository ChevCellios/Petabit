for(let attempt=0;attempt<30;attempt++) {
    try { const response=await fetch('http://127.0.0.1:3000/health/live',{signal:AbortSignal.timeout(1000)});if(response.status===200)process.exit(0); } catch {}
    await new Promise(resolve=>setTimeout(resolve,1000));
}
throw new Error('Private security lab did not become ready.');
