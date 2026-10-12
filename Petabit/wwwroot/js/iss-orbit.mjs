const radians = Math.PI / 180;
const earthRotation = 2 * Math.PI / 86164;
const clamp = value => Math.max(-1, Math.min(1, value));

function validPoint(point) {
    return point && Number.isFinite(point.latitude) && Math.abs(point.latitude) <= 90
        && Number.isFinite(point.longitude) && Math.abs(point.longitude) <= 180;
}

function vector(point, longitudeOffset = 0) {
    const latitude = point.latitude * radians;
    const longitude = point.longitude * radians + longitudeOffset;
    return [Math.cos(latitude) * Math.cos(longitude), Math.cos(latitude) * Math.sin(longitude), Math.sin(latitude)];
}

// Orient an illustrative orbit using two source positions, including Earth's
// rotation between them. This is a local plane estimate, not an ephemeris.
export function createIllustrativeOrbit(position) {
    const previous = position?.previousPosition;
    if (!validPoint(position) || !validPoint(previous)) return null;
    const elapsed = (Date.parse(position.positionUpdatedAt) - Date.parse(previous.positionUpdatedAt)) / 1000;
    if (!Number.isFinite(elapsed) || elapsed < 1 || elapsed > 120) return null;

    const current = vector(position);
    const earlier = vector(previous, -earthRotation * elapsed);
    const cosine = clamp(current.reduce((sum, value, index) => sum + value * earlier[index], 0));
    const angle = Math.acos(cosine);
    const angularSpeed = angle / elapsed;
    // Reject duplicate/mismatched samples instead of inventing a direction.
    if (angularSpeed < 0.0008 || angularSpeed > 0.0014) return null;
    const tangent = current.map((value, index) => (value * cosine - earlier[index]) / Math.sin(angle));

    return offset => {
        const point = current.map((value, index) => value * Math.cos(offset) + tangent[index] * Math.sin(offset));
        const longitude = Math.atan2(point[1], point[0]) - earthRotation * offset / angularSpeed;
        return {
            latitude: Math.asin(clamp(point[2])) / radians,
            longitude: ((longitude / radians + 540) % 360 + 360) % 360 - 180
        };
    };
}
