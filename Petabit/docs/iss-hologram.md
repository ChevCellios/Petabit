# ISS hologram

`/Home/Data` requests the current and preceding (30 seconds earlier) ISS positions
in one call to the [WTIA positions API](https://wheretheiss.at/w/developer).
The existing 10-second output cache, request limits and resilience policies still
apply. Displayed update time comes from the upstream Unix timestamp.

The globe marker uses the latest reported latitude/longitude. `iss-orbit.mjs`
orients an illustrative curve from the two points, compensating for Earth's
rotation between samples and fitting a local orbital plane. This handles both
northbound and southbound passes, turning points and the date line without a
fixed rotation or a permanently ascending arc. The observed angular speed is
used only for the illustration; this is not a TLE/SGP4 ephemeris or a predicted
flight path. Missing, duplicate, invalid or over-120-second sample pairs hide
the curve and arrow while retaining the valid position marker.

Both tracker panels reveal the existing page hologram through a translucent
surface. They share its 2 mm cells, wave and pointer response without another
animation loop. Reduced-motion preferences stop the wave. The Starlink globe
stage adds a subtle radial aura matching the ISS hologram in dark and light
themes.

`node --test Petabit.Tests/iss-orbit.test.mjs` covers both directions and
hemispheres, anchoring to both source positions, turning points, the date line,
and rejection of unreliable sample pairs. Application tests cover the upstream
batch request and timestamp validation.
