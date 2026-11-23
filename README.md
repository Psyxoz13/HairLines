# HairLines

Strand rendering, grooming, and GPU simulation for Unity 6.3 and URP. Hair is drawn as subpixel line primitives with depth-aware reconstruction and a Marschner shading model. Compute simulation supports XPBD, collisions, and reconstruction from guide strands. The Unity Editor tools include mask painting, combing, and guide sculpting.

## Setup

1. Open the repository with the Unity version recorded in ProjectSettings/ProjectVersion.txt.
2. Assign a URP asset in Graphics settings.
3. Use **Hair Lines → Install Renderer Feature**.
4. Select your mesh and use **Hair Lines → Create Hair**. With no selection the tool creates a spherical scalp.
5. Add a directional light.

[HairLines documentation](Assets/HairLines/README.md) · [Sample Distribution Shadows](Assets/SDSM/README.md)


## License

[PolyForm Noncommercial License 1.0.0](LICENSE.md).
The editor icons retain the licenses in Assets/HairLines/Editor/Icons/HairGuideTools/.
