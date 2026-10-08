export class OrbitalClock {
    constructor(wallTime = Date.now()) {
        this.anchorWall = wallTime;
        this.anchorSimulation = wallTime;
        this.speed = 1;
        this.running = false;
    }
    now(wallTime = Date.now()) {
        return this.anchorSimulation + (this.running ? (wallTime - this.anchorWall) * this.speed : 0);
    }
    configure(running, speed = this.speed, wallTime = Date.now()) {
        const simulation = this.now(wallTime);
        this.anchorSimulation = speed === 1 && (running || this.speed !== 1) ? wallTime : simulation;
        this.anchorWall = wallTime;
        this.running = running;
        this.speed = speed === 30 ? 30 : 1;
    }
}
