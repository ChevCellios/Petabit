(() => {
    const canvas = document.getElementById('page-hologram');
    const ctx = canvas.getContext('2d');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    let pointer; let frame; let lastMove = 0;
    function draw() {
        frame = undefined;
        const width = innerWidth, height = innerHeight;
        const light = document.body.classList.contains('light-mode');
        const rgb = light ? '0,100,111' : '94,221,203';
        ctx.clearRect(0,0,width,height);
        ctx.strokeStyle = `rgba(${rgb},${light ? .07 : .055})`; ctx.lineWidth = .6;
        const cell = 56;
        ctx.beginPath();
        for (let x=0;x<width;x+=cell) { ctx.moveTo(x,0); ctx.lineTo(x,height); }
        for (let y=0;y<height;y+=cell) { ctx.moveTo(0,y); ctx.lineTo(width,y); }
        ctx.stroke();
        if (pointer && !reduced.matches && !document.hidden) {
            const fade = Math.max(0,1-(performance.now()-lastMove)/1200);
            for(let x=Math.floor((pointer.x-160)/cell)*cell; x<pointer.x+160;x+=cell)
                for(let y=Math.floor((pointer.y-160)/cell)*cell; y<pointer.y+160;y+=cell) {
                    const alpha=Math.max(0,1-Math.hypot(x-pointer.x,y-pointer.y)/170)*fade;
                    ctx.strokeStyle=`rgba(${rgb},${alpha*.28})`; ctx.fillStyle=`rgba(${rgb},${alpha*.045})`;
                    ctx.fillRect(x+6,y+6,cell-12,cell-12); ctx.strokeRect(x+6,y+6,cell-12,cell-12);
                    ctx.beginPath(); ctx.moveTo(x+6,y+6); ctx.lineTo(x+12,y); ctx.lineTo(x+cell-6,y); ctx.lineTo(x+cell-6,y+cell-12); ctx.lineTo(x+cell-12,y+cell-6); ctx.stroke();
                }
            if(fade>0) frame=requestAnimationFrame(draw);
        }
    }
    function resize() {
        const ratio = Math.min(devicePixelRatio||1,1.5);
        canvas.width=innerWidth*ratio; canvas.height=innerHeight*ratio;
        ctx.setTransform(ratio,0,0,ratio,0,0); cancelAnimationFrame(frame); draw();
    }
    window.addEventListener('pointermove', e => { if(e.pointerType==='touch') return; pointer={x:e.clientX,y:e.clientY}; lastMove=performance.now(); if(!frame) frame=requestAnimationFrame(draw); },{passive:true});
    window.addEventListener('resize',resize,{passive:true});
    document.addEventListener('visibilitychange',()=>{cancelAnimationFrame(frame); frame=undefined; if(!document.hidden) draw();});
    document.getElementById('toggle-dark-mode')?.addEventListener('click',()=>{cancelAnimationFrame(frame); frame=requestAnimationFrame(draw);});
    document.addEventListener('DOMContentLoaded',resize);
    reduced.addEventListener('change',resize); resize();
})();
