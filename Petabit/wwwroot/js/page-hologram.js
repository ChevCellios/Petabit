(() => {
    const canvas = document.getElementById('page-hologram');
    const ctx = canvas.getContext('2d');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    // CSS reference millimetres: actual physical size depends on the display.
    const cell = 2 * 96 / 25.4;
    const base = document.createElement('canvas');
    let pointer, frame, lastPaint = 0, palette;
    canvas.dataset.cellSize = '2mm';
    function rebuild() {
        palette = document.body.classList.contains('light-mode')
            ? ['0,123,97','115,51,166','149,103,0']
            : ['94,224,192','189,133,255','255,208,117'];
        base.width = canvas.width; base.height = canvas.height;
        const b = base.getContext('2d');
        const ratio = Math.min(devicePixelRatio || 1, 1.5);
        b.setTransform(ratio,0,0,ratio,0,0);
        b.strokeStyle = `rgba(${palette[0]},.09)`; b.lineWidth = .5;
        b.beginPath();
        for (let x=0; x<innerWidth; x+=cell) { b.moveTo(x,0); b.lineTo(x,innerHeight); }
        for (let y=0; y<innerHeight; y+=cell) { b.moveTo(0,y); b.lineTo(innerWidth,y); }
        b.stroke();
    }
    function paintCells(left,right,top,bottom,strength,color) {
        for(let x=Math.max(0,Math.floor(left/cell)*cell);x<Math.min(innerWidth,right);x+=cell)
            for(let y=Math.max(0,Math.floor(top/cell)*cell);y<Math.min(innerHeight,bottom);y+=cell) {
                const a=strength(x,y); if(a<.005) continue;
                ctx.fillStyle=`rgba(${palette[color(x,y)]},${a})`;
                ctx.fillRect(x+1,y+1,cell-2,cell-2);
            }
    }
    function draw(time=performance.now()) {
        frame=undefined;
        if(document.hidden) return;
        if(time-lastPaint>=40 || reduced.matches) {
            lastPaint=time;
            ctx.clearRect(0,0,innerWidth,innerHeight);
            ctx.drawImage(base,0,0,innerWidth,innerHeight);
            if(!reduced.matches) {
                const wave=(time*.045)%(innerWidth+280)-140;
                paintCells(wave-110,wave+110,0,innerHeight,
                    (x,y)=>Math.max(0,1-Math.abs(x-wave-Math.sin(y*.012)*18)/110)*.12,
                    (x,y)=>Math.floor(y/160)%3);
            }
            if(pointer) paintCells(pointer.x-95,pointer.x+95,pointer.y-95,pointer.y+95,
                (x,y)=>Math.max(0,1-Math.hypot(x-pointer.x,y-pointer.y)/95)*.42,
                (x,y)=>(Math.floor(x/cell)+Math.floor(y/cell))%3);
        }
        if(!reduced.matches) frame=requestAnimationFrame(draw);
    }
    function restart() { cancelAnimationFrame(frame); lastPaint=0; draw(); }
    function resize() {
        const ratio=Math.min(devicePixelRatio||1,1.5);
        canvas.width=Math.round(innerWidth*ratio); canvas.height=Math.round(innerHeight*ratio);
        ctx.setTransform(ratio,0,0,ratio,0,0); rebuild(); restart();
    }
    window.addEventListener('pointermove',e=>{if(e.pointerType==='touch')return;pointer={x:e.clientX,y:e.clientY};if(reduced.matches)restart();},{passive:true});
    document.addEventListener('pointerleave',()=>{pointer=undefined;if(reduced.matches)restart();});
    window.addEventListener('resize',resize,{passive:true});
    document.addEventListener('visibilitychange',restart);
    window.addEventListener('pagehide',()=>cancelAnimationFrame(frame));
    document.getElementById('toggle-dark-mode')?.addEventListener('click',()=>{rebuild();restart();});
    document.addEventListener('DOMContentLoaded',resize);
    reduced.addEventListener('change',restart); resize();
})();
