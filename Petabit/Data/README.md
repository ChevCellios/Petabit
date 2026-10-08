# Starlink recovery snapshot

Public CelesTrak data retrieved at 2026-10-08T12:29:06.8865001Z through StarlinkService and validated against SATCAT. Compressed JSON retains the original retrieval timestamp and each NORAD identifier / catalog status.

Sources:
- https://celestrak.org/satcat/records.php?NAME=STARLINK&FORMAT=JSON
- https://celestrak.org/NORAD/elements/gp.php?GROUP=starlink&FORMAT=JSON

Uncompressed SHA-256: 0842d4124248e6bc58293db4618d957b988bc200cb481c1d3135817cb347cd5d

Catalog: 10838 operational, 294 partial, 2 non-operational, 0 other, 11134 total. Available elements: 10812 operational and 293 partial; no elements for the two non-operational satellites.

This is a startup recovery snapshot, not a live feed. It may replace only missing/older validated cache, expires for startup selection after seven days, and never changes source timestamps. Normal two-hour refresh and stale-data warnings remain active. Newer successful server snapshots supersede it. The file is outside wwwroot.
