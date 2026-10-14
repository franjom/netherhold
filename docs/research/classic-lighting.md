# Classic vertex lighting

Source: retail v1.70 DKII.EXE, Ghidra whole reads, 2026-10-14.
Existing material/terrain inputs are in kmf-materials.md, tag-highlight.md,
wall-pieces.md and wall-torches.md; this note owns their newly traced light helper.

## 1. Point-light contribution

STRONGLY INFERRED: 0x0057BF00 is a point-light accumulator, not a colour
clamp/packer. It loops over the supplied light list and computes distance
squared from the vertex to each light. Outside the light's squared radius
it adds nothing. Inside, it multiplies the light-direction/vertex-normal dot
product by a light-specific coefficient; negative-facing contributions are skipped.
It combines that factor with a lookup-table reciprocal distance, remaining
squared radius and another light coefficient. The resulting contribution
factor is capped at one, then the light's RGB is added to the running colour.
This caps one light's contribution; it does not clamp the accumulated RGB.

The vertex loop 0x0057B6D0 initializes colour from vertex light, material
brightness, instance tint and global ambient, then calls this helper with
the vertex position and normal before handing colour/UV to a render sink.
The original sum remains documented in tag-highlight.md section6.

UNRESOLVED: coefficient initialization, exact reciprocal-table boundaries,
final colour packing/transfer function, and matching URP light response.
Trace the selected colour sink and light-list builder before implementing
clamping, colour-space conversion or a replacement falloff shader.
No lighting implementation or matched-frame brightness result is claimed here.
