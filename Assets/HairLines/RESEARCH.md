# Сверка HairLines с опубликованным состоянием области

Пересмотр: 13 сентября 2026. Проверены рецензируемые статьи, доклады SIGGRAPH/EGSR/HPG,
официальная документация Unreal Engine, Microsoft, NVIDIA, AMD, SideFX, Autodesk и форматы
Alembic/USD. Локальные числа ниже не являются межплатформенным benchmark.

Текущая база: явные MeshTopology.Lines, 20 B/вершину, аппаратная линия 1 px,
supersampling ×1…4 + Gaussian resolve, opaque z/stochastic visibility, depth prepass на
desktop/console, Marschner R/TT/TRT + dual scattering, DOM на один главный directional light,
stable-prefix LOD, procedural guides/operators и GPU Jacobi/projective dynamics с KNN/SDF.

Локальный прогон (RTX 5070, D3D12, Unity 6000.3.20f1, 1920×1080, 200k strands):
s=1/2/3/4 дают 8.72/11.10/14.25/17.69 ms; после prepass BSDF+DOM sampling около 0.13 ms,
построение DOM около 4.10 ms. Это только приоритеты работ.

## Сводная таблица

| Подсистема | Что у нас | Лучшее сейчас (источник, год) | Вердикт | Что конкретно делать |
|---|---|---|---|---|
| 1. Растеризация | Hardware MeshTopology.Lines, depth prepass | Compute software rasterizer: cooperative assembly, DDA, atomicMin 64-bit/pixel, LOD (Lipp, Jarabo, Wimmer, Bode, 2026 [S1]); production software rasterizer (Kulikov, 2025 [S2]); mesh-shader hair mesh (Bhokare et al., 2024 [S3]). | отстаём (и чем заменить) | Hybrid: hardware lines close-up, compute DDA/visibility far/subpixel. |
| 2. AA | Supersampling + Gaussian, forward | Tojo et al., 2026 [S4]; neural coverage/tangent G-buffer + history + deferred shading, Wu et al., 2026, 0.8 ms static/1.0 dynamic [S5]. | расходится осознанно (и почему это оправдано) | Оставить Gaussian forward; neural path только вместе с deferred G-buffer. |
| 3. Прозрачность | Opaque z-buffer + stochastic existence | Frostbite visibility/MSAA/dedup, Tafuri 2019 [S6]; UE5.8 MSAA 1–8, PPLL до 32, дорогой [S7]; ShortCut bounded k-buffer, AMD 2016 [S8]. | расходится осознанно (и почему это оправдано) | Opaque оставить; при true translucency добавить k=2…4 mode. |
| 4. Отражение | Marschner + dual scattering + rim | Frostbite real-time baseline [S6]; Chiang near-field 2016 [S9]; microfacet BCSDF 2022 [S10]; vMF far-field filtering 2025 требует tangent map [S11]. | соответствует | Сохранить текущую модель. Предложенный «отдельный quality tier» отклонён — см. §12. |
| 5. Тени/пропускание | **DOM на главном свете + мировой воксельный объём плотности на остальных** | UE density voxel для shadow/transmittance/AO [S7]; dynamic-line sparse voxel + VCSV (Kraaijeveld et al., 2026 [S12]); RTX LSS optional [S13]. | **сделано, кроме AO ✅** | Осталось: AO по объёму; тень на сцене от дополнительных источников (нужен хук URP, которого нет). См. §5 |
| 6. LOD | Stable prefix, screen-width metric, coverage/count/depth compensation | Thick strands + aggregated BCSDF, Huang et al. 2025 [S14]; UE decimation/thickness/cards/meshes [S15]. | расходится осознанно (и почему это оправдано) | Перенести metric/compensation; thick strand требует другой геометрии. |
| 7. Корни | **Progressive weighted sample elimination по меш-скальпу; golden angle по сфере** | Progressive weighted sample elimination (Yuksel 2015 [S16], Bhokare et al. 2024 [S3]); PMJ/LDBN [S17][S18]; extensible Fibonacci sphere [S19]. | **сделано ✅** | — см. §7 ниже и README, «Корни расставляются исключением» |
| 8. Guides/clumps | Procedural growth, guides/operators, KNN | UE guides 5–10%, nearest/shape matching, RBF [S20]; physically guided interpolation <0.5 ms для >100k hairs на RTX4090 (Hsu et al. 2024 [S38]); Houdini/XGen [S21][S22]. | отстаём (и чем заменить) | Sparse render/sim guides + packed IDs/weights/RBF; хранить radius/tightness/stray/profile/seed. |
| 9. Симуляция | Jacobi/projective dynamics, stretch/bend, KNN, strain clamp | Stable Cosserat Rods: 1.465M vertices, 7 ms RTX3090 excl collisions [S23]; ADMM 0.18 s/16k, 5.6 s/128k [S24]; sag-free bake [S25][S25b]. | отстаём (и чем заменить) | Jacobi fallback + offline rest-shape optimization + Cosserat path для stiff/long chains. |
| 10. Коллизии/объём | Scene SDF + analytic primitives; KNN cohesion | UE voxelized velocity field [S26]; DER+MPM до 260 FPS (>2k strands, RTX3080) [S27]; TressFX dense SDF [S28]. | отстаём (и чем заменить) | Разделить KNN cohesion/contact; добавить voxel density/velocity или MPM. |
| 11. Память/формат | 20 B active state, float32 positions, custom/cyHair; нет Alembic/USD | HAIR points 12 B без solver state [S29]; Alembic cache [S30]; USD BasisCurves [S31]; RenderMan XPU 3–5x compression, BVH 437→246 MB [S32]. | отстаём (и чем заменить) | Разделить solver/render/interchange; adapters, quantize только render-side. |
| 12. Mobile/console | Только prepass/half, нет tiers/caps/fallback | Indiana Jones: Series S 2.39 ms, X 1.94, PS5 1.95, Pro 1.64; 65k/32k strands, 2M segments, 131 MB low [S2]. UE strands не mobile/Switch [S33]. | **расходится осознанно** — см. §12 | Ничего. Тиры и потолки параметров в этом ассете запрещены по решению проекта. |

## Детали по отстающим пунктам

### 1. Hybrid software rasterization

Lipp et al. показывают до 75% helper-lane waste в 2x2 quad для одно-пиксельных strands [S1].
Их pipeline собирает strand в workgroup, считает tangents, рисует edges DDA, пишет depth и
packed attributes через atomicMin в 64-bit/pixel buffer, затем shade/filter. В RTX5080 test
software+LOD+filter был около 3.35 ms close и 0.36 ms far, но это implicit hair mesh, не
наш explicit line model. Переносим DDA, packed visibility, indirect dispatch и footprint LOD;
bundle texture и deferred layout не переносим. Hardware path оставить для close-up. D3D12
conservative rasterization для lines/points undefined [S34], значит нужен compute capsule/DDA
либо triangle expansion.

### 5. Dynamic density volume — сделано ✅, кроме AO

Реализовано в `HairDensityVolume.compute` и `HairDensityVolumeSampling.hlsl`. На кадр:
обнуление -> DDA по каждому сегменту (Amanatides и Woo) с атомарным накоплением длины волокна
в fixed point -> деление на размер вокселя -> ray march на 24 шага из шейдера прядей для
каждого дополнительного источника. Один объём на грум, все объёмы — слои одного 3D-атласа
вдоль X. DOM остался на главном свете, как в [S7].

**Что из [S12] не перенесено и почему.** Отсечение по видимым камере вокселям (VCSV) —
центральная оптимизация их работы, и она к теням не применима: воксель за головой камере не
виден **именно потому**, что отбрасывает тень. Их per-voxel lists и второй проход воксeлизации
существуют ради трассировки, а не ради накопления плотности. Перенесены DDA, атомарное
накопление и посегментная воксeлизация каждый кадр. Их абсолютные числа (Apple M3, 128³,
Brain200k 5.2/1.3/7.7/1.3 ms) — tractography, не волосы, и переносить их было нельзя; замерено
своё: 100 000 прядей, 1.2 млн сегментов, RTX 5070 — построение **+0.32 мс** при 64 вокселях и
**+0.57 мс** при 128, марш меньше 0.05 мс на источник при 1280×720.

**Разрежённые кирпичи не делались.** 64³ на грум — это 1.5 МБ, 128³ — 12 МБ; префиксные суммы и
второй проход воксeлизации ради этого не окупаются. Jansson et al. называют 62.71 МБ на ponytail
при 256³/8 бит [S35] — на порядок больше, потому что у них один объём на всю сцену, а не куб по
габаритам грума.

**Модель пропускания та же, что у карты.** Хранится длина волокна на воксель; при резолве она
делится на размер вокселя, то есть получается «сколько волокон встретит луч, пересёкший воксель
насквозь», и это число уходит в тот же `exp(n·log(1−d/s))`. Проверено: 100 000 прядей по 0.22 м
дали в объёме 22 684.76 м волокна.

Замер качества (сцена освещена одним точечным светом, отдельно половина к свету и половина за
головой): затенённая сторона 196.06 -> 68.06, отношение 1.11 -> 2.01…2.12, и почти без
зависимости от разрешения.

**Осталось:** ambient occlusion по объёму — [S7] использует его и для этого. И тень волос от
дополнительных источников **на сцене**: экранный проход складывается в терм главного света, а
у URP нет места для такого же терма на каждый дополнительный источник.

### 7. Progressive root ordering — сделано ✅

Area CDF и low-discrepancy sequence дают детерминированный stable prefix, но после density mask
и UV-to-mesh mapping не гарантируют blue-noise minimum distance. Hair-mesh pipeline Bhokare et al.
использует заранее рассчитанный 2D progressive blue-noise set: любой prefix остаётся blue-noise
[S3]. Weighted sample elimination Yuksel [S16] строит такой порядок из избыточного пула
кандидатов на произвольном domain — на самом domain, а не в единичном квадрате, поэтому PMJ
и LDBN [S17][S18] здесь не подходят: их стратификация ломается ровно на mapping через CDF.

Реализовано в `HairSampleElimination` по [S16]: пул в 4× кандидатов той же детерминированной
последовательностью с тем же отбором по маске, вес соседа `(1 − d/d_max)⁸` с `α=8, β=0.65,
γ=1.5` из статьи, `d_max = 2·√(A / (2√3·N))` из площади скальпа, адаптивная плотность как
`d_max/√ρ`. Дальше прогрессивное расширение той же статьи: набор половинится, и выбывшие на
каждом шаге занимают хвост своего диапазона.

Замер на сфере 768 треугольников (среднее / минимум расстояния до ближайшего соседа, в долях
диаметра упаковки — 0.465 у пуассоновского процесса):

| префикс | последовательность | исключение |
|---|---|---|
| 100 % | 0.537 / 0.025 | 0.781 / 0.684 |
| 50 % | 0.545 / 0.027 | 0.741 / 0.626 |
| 25 % | 0.535 / 0.043 | 0.756 / 0.641 |
| 12.5 % | 0.524 / 0.030 | 0.748 / 0.628 |
| 6.25 % | 0.555 / 0.039 | 0.752 / 0.635 |

Качество префикса не проседает — это то, что читает LOD.

Стоимость — 2.1 с на 100 000 корней (Burst, `CompileSynchronously`), то есть bake, как и
сказано в источнике. Платится она один раз: результат кешируется на группу по скальпу, его
трансформу, сабмешу, содержимому маски и сиду, а количество в ключ не входит — уменьшение
числа прядей это префикс уже посчитанного списка. Замер пересборки 80 000 прядей: холодная
3.1 с, без изменений 0.12 с, смена сида 2.5 с, 80 000 → 40 000 0.05 с, обратно 0.11 с.
В плеере грум приходит из `HairGroomAsset`, где корни уже запечены.

Persistent root ID отдельным полем не заводился: привязка (треугольник + барицентрика) и есть
идентификатор корня, и она уже хранится в запечённом груме.

Golden-angle/E-SFG остался детерминированным O(1) путём для аналитической сферы, с явно
принятой ring/pole structure.

### 8. Guides and clumps

UE uses roughly 5–10% guides and transfers motion using nearest/shape matching, root/index/distance
metrics or RBF; Use Unique Guide reduces work [S20]. Для HairLines хранить per-render-strand
4 guide IDs + weights packed uint16/unorm16, root binding и local frame; solver обновляет guides,
compute pass восстанавливает render points. Для skinned heads добавить rest/current RBF или
skin-space biharmonic weights.

Hsu et al. интерполируют не только позиции guides, а внутренние силы и затем восстанавливают
render strands с constraint/SDF/drift correction, без training и precompute [S38]. На RTX4090
128 guides -> 106,686 hairs/1.707M vertices: linear hair skinning 0.283 ms, их метод 0.346 ms;
для опубликованных сцен overhead 17–23%, крупнейшая остаётся <0.5 ms. Это применимо только если
solver хранит guide material/force state; для статического procedural groom nearest/LHS остаётся
дешёвым fallback. TressFX 5.0 документирует guide/follow hairs и SDF collision [S28], но guide
от 2022 года относится к UE4.26/4.27 и не доказывает актуальную UE5 integration.

Clump data: center/guide ID, influence radius, tightness, root-tip profile, stray fraction, curl
amplitude/frequency, seed. Houdini публикует blend/radius/tightness/stray/fractal feedback/curl
ramps [S21]; XGen — Clump Scale, Volumize, Variance, Flatness, Offset, Curl, Copy, Noise [S22];
Ornatrix документирует guide/stem re-clumping [S36]. Это authoring controls, не runtime budget.

### 9. Convergence and sag-free initialization

Jacobi/projective dynamics остаётся cheap fallback, но corrections медленно распространяются
по длинной цепи. Stable Cosserat Rods разделяет position/rotation, решает orientation
quasi-static closed-form GS, positions parallel Jacobi; 1.465M hair vertices — 7 ms/frame RTX3090
без collisions [S23]. Добавить pre-bake sag-free rest lengths/orientations/Darboux vectors
[S25] и выбирать Cosserat для stiff/long chains. ADMM оставить offline: опубликованные 0.18 s/16k
и 5.6 s/128k не runtime budget [S24].

### 10. Hair-hair volume

KNN — cohesion regularizer, не полноценный contact broad phase и не гарантия объёма. Оставить
его как Cohesion; добавить regular voxel velocity/density field (как UE [S26]) либо MPM
P2G->grid->G2P [S27], correction один раз за substep, затем stretch/bend. Scene SDF оставить
для тела. TressFX dense SDF требует заранее покрыть animation bounds и выделить память [S28].

### 11. Solver state versus interchange

20 B defensible для mutable solver record. HAIR хранит points/visual attrs, но не velocity,
lambda/rest frames [S29]; Alembic — cache, не live scene graph [S30]; USD BasisCurves задаёт
points/counts/basis/wrap/widths, но не GPU packing [S31]. Сделать три слоя: mutable solver,
compressed render cache (block bounds/quantized positions/tangents/BVH), interchange adapters.
Включать quantization только после проверки screen displacement/silhouette/energy. RenderMan XPU
показывает практичность render-side compression, но не сравним с active solver [S32].

### 12. Platform tiers — расходится осознанно

Indiana Jones: Series S 2.39 ms at 1080p (sim .49, skin .17, cull .10, raster .61, shade .62,
comp .40), Series X 1.94 ms at 1800p, PS5 1.95 ms at 1800p, Pro 1.64 ms at 2160p;
65k high, 32k low, 2M segments/frame, 2000 segments/tile,
131 MB fixed low-spec [S2]. UE confirms no strands on mobile/Switch [S33]. Primary Android/iOS
hair frame-time publication не найдена.

**Рекомендация «добавить High/Mid/Low capability table, hard caps, cards/mesh fallback»
отклонена.** Это решение проекта, а не упущение: одинаковое качество на всех платформах, без
пресетов, тиров и урезания параметров; ветвление допускается только там, где реализация
действительно другая (сейчас таких мест два — depth prepass и `half`). То же относится к
предложению завести «отдельный quality tier» под новые BCSDF в пункте 4: если модель лучше,
она становится единственной, а если нет — её не берут вовсе.

Что из этого пункта остаётся в силе и не отклонено: **нельзя переносить консольные ms на
tiled GPU и нельзя считать `half` универсально более быстрым** — это замечание про
достоверность чисел, и оно принято.

Полезная часть — не тиры, а то, что число прядей и так уже непрерывная величина: LOD выбирает
долю по экранной ширине (§6), а корни упорядочены прогрессивно (§7), поэтому «65k high /
32k low» здесь получается само собой из расстояния и разрешения, без таблицы.

## Порядок работ

Из кадра 8.72 мс (200k, s=1) построение DOM занимает 4.10 мс, остальное — растеризация.
Сразу оговорка, чтобы порядок не читался неверно: **воксельный объём не убирает эти 4.10 мс.**
И UE [S7], и сам §5 оставляют deep shadow map на главном свете, а объём обслуживает остальные
источники и AO. То есть §5 — это возможности, а не миллисекунды, и он к кадру добавляет.
Миллисекунды лежат в §1.

1. ~~**§5, воксельный объём плотности.**~~ — **сделано**, см. §5 выше. Волосы затеняют себя от
   любого источника, а не только от главного направленного. Осталось от пункта: ambient
   occlusion по объёму, и тень на сцене от дополнительных источников (упирается в URP).
2. **§1, гибридная растеризация.** Здесь лежат оставшиеся 4.6 мс кадра, и это единственный
   пункт, который их уменьшает. Он же самый рискованный: у [S1] pipeline построен на implicit
   hair mesh, переносимы DDA, packed visibility, indirect dispatch и footprint LOD, а bundle
   texture и deferred layout — нет. Аппаратный путь остаётся на ближнем плане.
3. **§9, sag-free инициализация.** Отдельно от Cosserat: [S25] это pre-bake длин покоя и
   Darboux-векторов, он не меняет солвер и даёт видимый результат сразу. Cosserat [S23] —
   отдельным путём для жёстких длинных цепей, после.
4. **§8, разрежённые guides и интерполяция.** Меняет архитектуру груминга целиком, поэтому
   поздно. [S38] применим только если солвер хранит guide material/force state.
5. **§10, объём волос.** Опирается на структуру из §5, поэтому после него.
6. **§11, три слоя памяти и Alembic/USD.** Не влияет на кадр; квантование включать только
   после проверки silhouette/energy, как сказано в §11.

---

## Качество волос: что ещё не использовано (15 сентября 2026)

Сверка кода со статьёй [S4] и с тем, что есть у UE/pbrt. Уже сделано и сюда не входит: сужение
кончика через покрытие, sky occlusion по объёму, dual scattering + rim, depth-aware резолв.

**Из статьи пропущено одно:** радиенс на вершину в SH степени 3 (§5 [S4]) — у нас режим
`Radiance` берёт только RGB. Нужно лишь для импорта восстановленных моделей из репозитория
авторов, на выращенный грум не влияет; cyHair/obj SH не несут, понадобится свой формат. Всё
остальное из прямого рендера (сетка 2×2 с фиксированными позициями, Гаусс σ 0.5/срез 1.5,
τ = 0.5, перспективно-корректная интерполяция) реализовано.

**Вне статьи, по убыванию пользы** — все три сделаны 15.09 и стоят открытыми A/B, см.
README («Векторы движения», таблица материала):

1. **Motion vectors прядей + сдвиг субпиксельной сетки по кадрам.** Процедурный draw векторов
   движения не пишет, поэтому с TAA/STP/DLSS движущиеся волосы будут шлейфить (сейчас на
   камерах AA выключен, дефект спит). С векторами временное накопление даёт больше выборок при
   том же *s* и гасит мерцание прядей тоньше пикселя. Прошлые позиции уже есть в солвере.
   Источники: TSR/TAA требуют velocity у движущейся геометрии [S39]; история кадров как основа
   AA волос [S5]. Цена: отдельный проход или MRT в проход прядей.
2. **Разброс BSDF по пряди** — наклон кутикулы и шероховатость от хеша пряди. Сейчас на пряди
   меняется только яркость, поэтому блик R/TRT идёт ровной полосой. Приём — shift-текстура
   Scheuermann (ATI, 2004) [S40]; в UE тот же смысл у `Seed` и `Roughness` на точку в
   `Hair Attributes` [S41]. Цена: хеш на фрагмент.
3. **Цвет из концентрации меланина** (эумеланин/феомеланин → σa) вместо RGB корня/кончика.
   Даёт физически согласованный оттенок TT/TRT и естественный разброс цвета по прядям. Источник:
   Chiang et al. 2016 [S9], d'Eon et al. 2011 [S42], реализация в pbrt [S43]. Цена: нулевая,
   пересчёт на CPU.

---

## Источники

[S1] Lipp, Jarabo, Wimmer, Bode (2026), [paper](https://arxiv.org/html/2607.04230), DOI [10.1145/3820015](https://doi.org/10.1145/3820015).  
[S2] Kulikov/MachineGames (2025), [Indiana Jones slides](https://advances.realtimerendering.com/s2025/content/Strand%20Hair%20in%20IJGC%20-%20Final%20Slides%20%28Post-Conference%29.pdf).  
[S3] Bhokare, Montalvo, Diaz, Yuksel (2024), [hair meshes](https://cemyuksel.com/research/hairmesh_rendering/Real-Time_Hair_Rendering_with_Hair_Meshes-SIGGRAPH24.pdf), DOI [10.1145/3641519.3657521](https://doi.org/10.1145/3641519.3657521).  
[S4] Tojo, Shamir, Umetani, Bickel (2026), [line primitives](https://arxiv.org/html/2609.00625), DOI [10.1145/3842527](https://doi.org/10.1145/3842527).  
[S5] Wu, Shen, Huang, Yan, Montazeri, Wu (2026), [neural AA](https://arxiv.org/html/2605.17557).  
[S6] Tafuri (2019), [Frostbite](https://advances.realtimerendering.com/s2019/hair_presentation_final.pdf).  
[S7] Epic Games (2026), [groom scalability](https://dev.epicgames.com/documentation/en-us/unreal-engine/groom-scalability-and-performance-with-unreal-engine).  
[S8] AMD GPUOpen (2016), [ShortCut](https://gpuopen.com/news/tressfx-3-1/).  
[S9] Chiang et al. (2016), [hair model](https://onlinelibrary.wiley.com/doi/abs/10.1111/cgf.12830).  
[S10] Huang, Hullin, Hanika (2022), [microfacet hair](https://onlinelibrary.wiley.com/doi/full/10.1111/cgf.14588).  
[S11] Wei, Song (2025), [far-field BCSDF](https://www.mdpi.com/2313-433X/11/5/158).  
[S12] Kraaijeveld et al. (2026), [dynamic line voxels](https://onlinelibrary.wiley.com/doi/10.1111/cgf.70372).  
[S13] Hart, Kozlowski (2025), [NVIDIA LSS](https://developer.nvidia.com/blog/render-path-traced-hair-in-real-time-with-nvidia-geforce-rtx-50-series-gpus/).  
[S14] Huang et al. (2025), [hair LOD](https://kuiwuchn.github.io/hairlod/hairlod.pdf), DOI [10.1111/cgf.70181](https://doi.org/10.1111/cgf.70181).  
[S15] Epic Games (2026), [groom LOD](https://dev.epicgames.com/documentation/en-us/unreal-engine/setting-up-level-of-detail-for-grooms-in-unreal-engine).  
[S16] Yuksel (2015), [sample elimination](https://www.cemyuksel.com/research/sampleelimination/).  
[S17] Christensen, Kensler, Kilpatrick (2018), [PMJ](https://graphics.pixar.com/library/ProgressiveMultiJitteredSampling/paper.pdf).  
[S18] Perrier et al. (2018), [low-discrepancy blue noise](https://doi.org/10.1111/cgf.13366).  
[S19] Marques et al. (2021), [spherical Fibonacci](https://pubmed.ncbi.nlm.nih.gov/31722480/).  
[S20] Epic Games (2026), [interpolation](https://dev.epicgames.com/documentation/en-us/unreal-engine/groom-interpolation-in-unreal-engine), [physics](https://dev.epicgames.com/documentation/en-us/unreal-engine/hair-physics-in-unreal-engine---overview).  
[S21] SideFX (2026), [Hair Clump](https://www.sidefx.com/docs/houdini/nodes/sop/hairclump.html).  
[S22] Autodesk (current), [XGen Clump](https://help.autodesk.com/cloudhelp/2017/ENU/Maya/files/GUID-809D1ADA-4D78-45E5-9900-9997089DFC3F.htm).  
[S23] Hsu, Wang, Wu, Yuksel (2025), [Cosserat Rods](https://www.cemyuksel.com/research/papers/Stable_Cosserat_Rods-SIGGRAPH25.pdf), DOI [10.1145/3721238.3730618](https://doi.org/10.1145/3721238.3730618).  
[S24] Daviet (2023), [ADMM](https://research.nvidia.com/labs/prl/publication/daviet23/), DOI [10.1145/3588432.3591551](https://doi.org/10.1145/3588432.3591551).  
[S25] Hsu et al. (2023), [sag-free](https://graphics.cs.utah.edu/research/projects/sag-free-hair/sig23_sagfree-hair.pdf).
[S25b] Takahashi, Batty (2025), [Rest Shape Optimization for Sag-Free Discrete Elastic Rods](https://doi.org/10.1111/cgf.70019), [open PDF](https://cs.uwaterloo.ca/~c2batty/papers/Takahashi2025/rest_shape_optimization.pdf).
[S26] Epic Games (2026), [hair physics](https://dev.epicgames.com/documentation/en-us/unreal-engine/hair-physics-in-unreal-engine---overview).  
[S27] Huang et al. (2023), [DER+MPM](https://lihuang.work/pdf/realtime_hair/realtime_hair.pdf).  
[S28] AMD GPUOpen (2022), [TressFX 5.0 Developer Guide](https://gpuopen.com/download/TressFX5.0_DeveloperGuide.pdf) (UE4.26/4.27 integration).  
[S29] Yuksel, undated, [HAIR](https://www.cemyuksel.com/research/hairmodels/).  
[S30] Alembic, [reference](https://docs.alembic.io/reference/).  
[S31] Pixar/OpenUSD, [BasisCurves](https://openusd.org/dev/api/class_usd_geom_basis_curves.html).  
[S32] Christensen et al. (2025), [RenderMan XPU](https://graphics.pixar.com/library/RenderManXPU/paper.pdf).  
[S33] Epic Games (2026), [platform support](https://dev.epicgames.com/documentation/en-us/unreal-engine/groom-platform-support-in-unreal-engine).  
[S34] Microsoft (2021), [conservative rasterization](https://learn.microsoft.com/en-us/windows/win32/direct3d12/conservative-rasterization).  
[S35] Jansson, Chajdas, Lacroix, Ragnemalm (2019), [hybrid hair](https://diglib.eg.org/bitstream/handle/10.2312/sr20191215/001-008.pdf).  
[S36] Ephere (2026), [Ornatrix Clump](https://ephere.com/plugins/epic/ue/ornatrix/docs/1/Clump.html).  
[S37] Stuyck et al. (2025), [Quaffure](https://openaccess.thecvf.com/content/CVPR2025/papers/Stuyck_Quaffure_Real-Time_Quasi-Static_Neural_Hair_Simulation_CVPR_2025_paper.pdf); Lin et al. (2026), [Neuralocks](https://arxiv.org/abs/2507.05191).
[S38] Hsu, Wang, Pan, Gao, Yuksel, Wu (2024), [physically guided hair interpolation](https://graphics.cs.utah.edu/research/projects/physically-guided-hair-interpolation/hairinterp-siggraph2024.pdf), DOI [10.1145/3658176](https://doi.org/10.1145/3658176).
[S39] Epic Games (2026), [Temporal Super Resolution](https://dev.epicgames.com/documentation/unreal-engine/temporal-super-resolution-in-unreal-engine?lang=en-US).
[S40] Scheuermann (2004), Practical Real-Time Hair Rendering and Shading, ATI, SIGGRAPH 2004 sketch.
[S41] Epic Games (2026), [groom materials, Hair Attributes](https://dev.epicgames.com/documentation/unreal-engine/groom-materials-in-unreal-engine).
[S42] d'Eon, Francois, Hill, Letteri, Aubry (2011), An Energy-Conserving Hair Reflectance Model, EGSR.
[S43] Pharr, Jakob, Humphreys, pbrt-v4, [hair scattering](https://pbr-book.org/4ed/Reflection_Models/Scattering_from_Hair).

Не выдумывалась отдельная стоимость dynamic hair voxel rebuild: UE/TressFX/Indiana Jones её не
публикуют. Абсолютные ms разных работ нельзя переносить на Unity lines без matching benchmark.
