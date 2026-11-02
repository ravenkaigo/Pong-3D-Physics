const assert = require("assert");

function calculateNetClearanceVy(startY, startZ, vz, hitArcLift = 3.2, clearanceY = 0.55, g = 9.81) {
    if (Math.abs(vz) < 0.1) return hitArcLift;
    const distToNet = Math.abs(startZ);
    const tNet = distToNet / Math.abs(vz);
    if (tNet < 0.04) return hitArcLift;
    const gravityDrop = 0.5 * g * Math.pow(tNet, 2);
    const neededVy = (clearanceY - startY + gravityDrop) / tNet;
    return Math.max(hitArcLift, Math.min(neededVy, 7.5));
}

function calcServeLaunch(paddlePos, paddleVel, servePaddleOffset, initialSpeed = 13.5, hitArcLift = 3.2, transfer = 0.5) {
    const ballPos = {
        x: paddlePos.x + servePaddleOffset.x,
        y: paddlePos.y + servePaddleOffset.y,
        z: paddlePos.z + servePaddleOffset.z,
    };
    const vz = Math.max(initialSpeed + Math.max(0, paddleVel.z) * transfer, 13.5);
    const vyReq = calculateNetClearanceVy(ballPos.y, ballPos.z, vz, hitArcLift);
    const vy = Math.max(hitArcLift + Math.max(0, paddleVel.y) * transfer, vyReq);
    const hitFactor = 0; // centered hit
    const vx = hitFactor * 4.0 + (paddleVel.x * transfer);
    return { ballPos, vx, vy, vz };
}

// 1. Stationary paddle serve test
const serveStatic = calcServeLaunch({ x: 0, y: 0.85, z: -7.0 }, { x: 0, y: 0, z: 0 }, { x: 0, y: 0.25, z: 0.75 });
assert.strictEqual(serveStatic.ballPos.z, -6.25);
assert.strictEqual(serveStatic.ballPos.y, 1.10);
assert.ok(serveStatic.vz >= 13.5);
assert.ok(serveStatic.vy >= 3.2);

// Parabolic trajectory verification at net (z = 0)
const tNet = Math.abs(serveStatic.ballPos.z) / serveStatic.vz;
const yAtNet = serveStatic.ballPos.y + (serveStatic.vy * tNet) - (0.5 * 9.81 * Math.pow(tNet, 2));
assert.ok(yAtNet >= 0.55, `Ball must clear 0.20m net: cleared at ${yAtNet.toFixed(3)}m`);

// 2. Forward smash stroke serve test
const serveSmash = calcServeLaunch({ x: 0, y: 0.85, z: -7.0 }, { x: 0, y: 2.0, z: 8.0 }, { x: 0, y: 0.25, z: 0.75 });
assert.ok(serveSmash.vz > serveStatic.vz, "Forward stroke should boost ball speed");
assert.ok(serveSmash.vy > serveStatic.vy, "Upward paddle velocity should boost arc");

console.log("All physics and serve ballistic tests passed!");
