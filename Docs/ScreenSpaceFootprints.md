# Screen Space Footprint Decal

本模块基于屏幕空间 Decal 思路，在 Unity URP14 中实现了一套运行时脚印贴花系统。

该系统不是直接使用 Unity 内置 Decal Projector，而是通过自定义 `ScriptableRendererFeature`、体积盒绘制、深度重建世界坐标和运行时脚步事件，完成角色移动时的脚印生成与淡出效果。

当前版本主要用于展示角色动画、屏幕空间贴花、URP Render Feature 和 Shader 数据流之间的结合。

实现内容包括：

- 自定义 Screen Space Decal Renderer Feature
- 使用体积盒 Cube 绘制 Decal 投射范围
- 通过 Camera Depth Texture 重建世界坐标
- 在 Shader 中判断像素是否位于 Decal 投射盒内部
- 支持贴花纹理、颜色、透明度、边缘淡出、距离淡出
- 支持多个 Decal Projector 同时绘制
- 基于角色脚骨骼位置生成脚印
- 使用 Animation Event 在落脚帧触发脚印
- 支持左右脚不同贴图
- 支持脚印生命周期与淡出销毁

---

## 1. 实现目标

本模块的目标是实现一套可用于角色移动痕迹表现的运行时脚印系统。

与直接在地面上生成 Quad 或使用内置 Decal 不同，本模块采用屏幕空间 Decal 方式：

```text
Character Animation Event
        ↓
Foot Bone Position
        ↓
Raycast Ground
        ↓
Create Decal Projector
        ↓
Custom URP Render Feature
        ↓
Draw Volume Box
        ↓
Sample Camera Depth
        ↓
Reconstruct World Position
        ↓
World Position → Decal Local Space
        ↓
Clip / Sample Texture / Fade
        ↓
Final Footprint Decal
```

这样做的重点是展示：

- 如何在 URP 中插入自定义 Render Pass
- 如何基于深度图重建世界坐标
- 如何模拟 Unity Decal Projector 的投射盒逻辑
- 如何将角色动画事件和渲染表现结合起来
- 如何让脚印作为运行时动态生成的视觉效果存在

---

## 2. 功能列表

- 使用 `ScriptableRendererFeature` 插入自定义屏幕空间 Decal Pass
- 使用 Cube Volume Box 表示 Decal 投射范围
- 使用 `_CameraDepthTexture` 重建当前像素的世界空间位置
- 使用 `world -> decal local` 矩阵判断像素是否落入 Decal Box
- 使用 local XY 作为贴花 UV 平面
- 使用 local Z 作为投射深度方向
- 支持 Decal Texture / Color / Opacity
- 支持 Edge Fade，减少贴花边缘硬切
- 支持 Distance Fade，根据相机距离控制贴花显示
- 支持 Angle Fade，根据表面法线和投射方向控制贴花
- 使用 `MaterialPropertyBlock` 为每个 Decal 单独传参
- 使用 `ActiveProjectors` 列表管理场景中的多个 Decal
- 使用 Humanoid 左右脚骨骼生成脚印
- 通过 Animation Event 在 Walking / Running 落脚帧触发脚印
- 支持左右脚不同 Footprint Texture
- 支持脚印停留一段时间后逐渐淡出并销毁

---

## 3. 效果展示

### 3.1 运行时脚印生成

角色移动时，系统会根据动画落脚事件生成脚印。

脚印并不是按照角色中心点固定间距生成，而是基于左右脚骨骼位置向地面进行 Raycast，因此脚印位置可以更接近真实落脚点。

特点：

- 左右脚交替生成
- 脚印方向跟随角色朝向
- 脚印贴合地面
- 支持走路和跑步动画
- 可配置脚印大小、偏移、淡出时间

建议配图：

```text
pictures/footprints/footprint_runtime.png
```

---

### 3.2 屏幕空间 Decal 投射

脚印本身由 Screen Space Decal Shader 绘制。

C# 侧只绘制一个 Decal Volume Box，Shader 通过屏幕 UV 采样深度，再重建场景表面世界坐标，最后判断这个世界坐标是否位于 Decal Box 内。

特点：

- 不需要为每个脚印生成地面 Mesh
- 可以投射到已有场景表面
- 支持多个脚印同时存在
- 可以统一控制透明度、边缘淡出和距离淡出

建议配图：

```text
pictures/footprints/footprint_decal_box.png
```

---

### 3.3 脚印淡出

每个脚印生成后会先保持一段时间，然后逐渐降低 opacity，最后销毁对应 GameObject。

特点：

- 避免场景中脚印无限累积
- 脚印消失过程更加自然
- 生命周期和淡出时间可调节

建议配图：

```text
pictures/footprints/footprint_fade.png
```

---

## 4. 技术实现

### 4.1 Decal Projector 数据结构

`ScreenSpaceDecalProjector` 用来保存单个 Decal 的数据。

主要数据包括：

- Decal Material
- Decal Texture
- Decal Color
- Decal Box Size
- Pivot
- UV Tiling / Offset
- Opacity
- Edge Fade
- Distance Fade
- Angle Fade

其中 Decal 的坐标约定为：

```text
local XY = 贴图平面
local Z  = 投射深度方向
```

每个启用的 Projector 会注册到全局列表中：

```csharp
public static readonly List<ScreenSpaceDecalProjector> ActiveProjectors
    = new List<ScreenSpaceDecalProjector>();
```

Renderer Feature 在渲染时会遍历这个列表，并为每个 Projector 绘制一次体积盒。

---

### 4.2 World To Decal Local 矩阵

Shader 需要判断当前像素重建出来的世界坐标是否位于 Decal Box 内部。

因此 C# 侧会计算一个矩阵：

```text
world position → decal normalized local position
```

转换后，如果 local position 的 x / y / z 都在：

```text
-0.5 ~ 0.5
```

范围内，就说明当前像素处于 Decal 投射盒内部。

整体流程：

```text
World Position
    ↓
Projector World To Local
    ↓
Apply Pivot
    ↓
Normalize By Size
    ↓
Decal Local Space
```

Shader 中再通过 `abs(localPos)` 判断是否超出盒子范围。

---

### 4.3 自定义 Renderer Feature

`ScreenSpaceDecalFeature` 继承自 `ScriptableRendererFeature`。

它负责：

- 创建自定义 `ScriptableRenderPass`
- 请求 `_CameraDepthTexture`
- 设置相机颜色目标
- 判断当前相机是否需要绘制 Decal
- 遍历所有启用的 Decal Projector
- 使用 `cmd.DrawMesh` 绘制 Decal Volume Box

当前 Pass 使用：

```csharp
ConfigureInput(ScriptableRenderPassInput.Depth);
```

这会告诉 URP 当前 Pass 需要相机深度图。

然后在 Execute 中，将 Decal 绘制到当前 Camera Color Target 上：

```text
Camera Color Target
        ↑
Screen Space Decal Pass
        ↑
Draw Volume Box
```

---

### 4.4 体积盒绘制

本模块没有使用全屏三角形，而是为每个 Decal 绘制一个 Cube Volume Box。

这样可以减少不必要的屏幕像素计算，因为只有体积盒覆盖到的屏幕区域才会执行 Fragment Shader。

体积盒 Mesh 是一个范围为：

```text
-0.5 ~ 0.5
```

的单位立方体。

绘制时通过矩阵将它转换为真实的 Decal 投射范围：

```text
Unit Cube
    ↓
Scale(size)
    ↓
Translate(pivot)
    ↓
Projector Local To World
    ↓
Volume Box In World
```

---

### 4.5 深度重建世界坐标

Shader 中首先根据当前片元屏幕位置计算 Screen UV：

```hlsl
float2 screenUV = positionCS.xy / _ScaledScreenParams.xy;
```

然后采样相机深度图：

```hlsl
float rawDepth = SAMPLE_TEXTURE2D_X(
    _CameraDepthTexture,
    sampler_CameraDepthTexture,
    screenUV
).r;
```

最后使用 URP 提供的函数重建世界坐标：

```hlsl
float3 worldPos = ComputeWorldSpacePosition(
    screenUV,
    rawDepth,
    UNITY_MATRIX_I_VP
);
```

屏幕空间 Decal 的核心就在这里。

它并不直接使用 Cube 表面的世界坐标，而是使用当前屏幕像素背后的场景表面世界坐标。

这样 Decal 最终看起来就像贴在地面或场景物体表面上。

---

### 4.6 Decal Box 判断与 UV 采样

重建出世界坐标后，将其转换到 Decal Local Space：

```hlsl
float3 decalLocalPos = mul(
    _DecalWorldToLocal,
    float4(worldPos, 1.0)
).xyz;
```

然后判断是否位于 Decal Box 内部：

```hlsl
if (absLocal.x > 0.5 || absLocal.y > 0.5 || absLocal.z > 0.5)
{
    discard;
}
```

如果当前像素在 Box 内，则使用 local XY 生成 UV：

```hlsl
float2 decalUV = decalLocalPos.xy + 0.5;
```

然后采样脚印贴图：

```hlsl
half4 decalTex = SAMPLE_TEXTURE2D(
    _DecalTexture,
    sampler_DecalTexture,
    decalUV
);
```

---

### 4.7 边缘淡出

为了避免 Decal 在投射盒边缘出现硬切，Shader 中根据当前点距离 XY 边缘的距离计算淡出：

```hlsl
float distToPlaneEdge = min(
    0.5 - absLocal.x,
    0.5 - absLocal.y
);
```

然后使用 `smoothstep` 做柔和过渡：

```hlsl
float boxFade = smoothstep(0.0, edgeFade, distToPlaneEdge);
```

最终将淡出结果乘到 alpha 上：

```hlsl
color.a *= boxFade;
```

---

### 4.8 角度淡出

为了减少 Decal 投射到不合理角度的表面上，本模块加入了 Angle Fade。

Shader 通过深度重建近似法线：

```hlsl
float3 normalWS = ReconstructWorldNormalFromDepth(worldPos);
```

然后计算表面法线与 Decal 投射反方向之间的夹角关系：

```hlsl
float facing = saturate(dot(normalWS, decalBackwardWS));
```

再根据角度范围做淡出：

```hlsl
float angleFade = smoothstep(cosEnd, cosStart, facing);
```

这样可以减少脚印投射到侧面或不应该出现的位置。

---

### 4.9 脚印生成逻辑

脚印生成由 `FootprintDecalSpawner` 负责。

当前实现不再根据角色中心点移动距离生成脚印，而是基于真实脚骨骼位置生成：

```text
LeftFoot / RightFoot Transform
        ↓
Raycast Down
        ↓
Hit Ground
        ↓
Calculate Surface Normal
        ↓
Calculate Footprint Direction
        ↓
Instantiate Decal Projector
```

系统会自动从 Humanoid Avatar 中获取左右脚骨骼：

```csharp
leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
```

当 Walking 或 Running 动画播放到落脚帧时，通过 Animation Event 调用：

```csharp
SpawnLeftFootprint();
SpawnRightFootprint();
```

然后根据对应脚骨骼位置生成 Decal。

---

### 4.10 脚印位置与方向

脚印位置通过 Raycast 命中的地面点计算：

```csharp
Vector3 rayOrigin = footTransform.position + Vector3.up * rayStartHeight;
Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, totalRayDistance, groundMask);
```

脚印方向使用角色朝向投影到地面平面上：

```csharp
Vector3 forwardOnSurface = Vector3.ProjectOnPlane(characterRoot.forward, normal);
```

最终生成位置：

```csharp
Vector3 spawnPosition =
    hit.point +
    forwardOnSurface * footForwardOffset +
    normal * surfaceOffset;
```

其中：

- `hit.point` 是地面命中点
- `footForwardOffset` 用来从脚踝偏移到脚掌中心
- `surfaceOffset` 用来避免贴花与地面深度过于接近造成闪烁

旋转方向使用：

```csharp
Quaternion.LookRotation(-normal, forwardOnSurface);
```

含义是：

```text
local +Z 指向地面内部
local +Y 对齐脚尖方向
```

---

### 4.11 脚印生命周期

每个脚印生成后，会开启一个 Coroutine 管理生命周期。

流程：

```text
Spawn Footprint
    ↓
Opacity = 1
    ↓
Wait Visible Time
    ↓
Fade Opacity To 0
    ↓
Destroy GameObject
```

这样可以避免脚印对象无限累积，也能让脚印逐渐消失。

---

## 5. 可调参数

主要参数暴露在 Inspector 中，方便调节脚印效果。

### 5.1 ScreenSpaceDecalProjector 参数

| 参数 | 作用 |
|---|---|
| `decalMaterial` | 当前 Decal 使用的材质 |
| `decalTexture` | 当前 Decal 使用的贴图 |
| `decalColor` | Decal 颜色 |
| `size` | Decal 投射盒尺寸 |
| `pivot` | Decal 投射盒中心偏移 |
| `tiling` | UV 平铺 |
| `offset` | UV 偏移 |
| `opacity` | 整体透明度 |
| `edgeFade` | 边缘淡出范围 |
| `drawDistance` | 最大绘制距离 |
| `startFade` | 距离淡出开始比例 |
| `angleFade` | 角度淡出范围 |

---

### 5.2 FootprintDecalSpawner 参数

| 参数 | 作用 |
|---|---|
| `characterRoot` | 角色根节点 |
| `animator` | 角色 Animator |
| `leftFoot` | 左脚骨骼 |
| `rightFoot` | 右脚骨骼 |
| `footprintPrefab` | 脚印 Decal Prefab |
| `leftFootTexture` | 左脚脚印贴图 |
| `rightFootTexture` | 右脚脚印贴图 |
| `groundMask` | 地面检测 Layer |
| `rayStartHeight` | Raycast 起点高度 |
| `rayDistance` | Raycast 检测距离 |
| `surfaceOffset` | 沿法线方向抬起的偏移 |
| `footForwardOffset` | 脚印向脚尖方向的偏移 |
| `footprintYawOffset` | 脚印贴图方向修正 |
| `footprintSize` | 脚印投射盒大小 |
| `footprintVisibleTime` | 脚印完整显示时间 |
| `footprintFadeTime` | 脚印淡出时间 |
| `minTimeBetweenSameFoot` | 同一只脚最小生成间隔 |

---

## 6. Debug 与调试思路

### 6.1 检查 Animation Event

脚印生成依赖 Walking / Running 动画中的 Animation Event。

建议检查：

- 左脚落地帧是否调用 `SpawnLeftFootprint`
- 右脚落地帧是否调用 `SpawnRightFootprint`
- 函数名大小写是否完全一致
- Idle 动画中不要添加脚印事件

如果脚印没有生成，可以在事件函数中加入：

```csharp
Debug.Log("Left Foot Event");
Debug.Log("Right Foot Event");
```

确认事件是否被 Animator 调用。

---

### 6.2 检查 Raycast

如果事件触发了但没有脚印，优先检查 Raycast：

- `groundMask` 是否包含地面 Layer
- `rayStartHeight` 是否足够高
- `rayDistance` 是否足够长
- 地面是否有 Collider
- 射线是否打到了角色自身

建议地面单独设置为 `Ground` Layer，避免 Raycast 检测到角色模型或其他无关物体。

---

### 6.3 检查 Decal 投射方向

如果脚印方向错误，可以调节：

```text
footprintYawOffset
```

常见修正值：

```text
0
90
-90
180
```

如果脚印整体偏后，可以调节：

```text
footForwardOffset
```

当前脚印是基于 `LeftFoot / RightFoot` 骨骼生成，而这些骨骼通常更接近脚踝位置，因此需要一定的前向偏移，让脚印中心更接近脚掌。

---

## 7. 性能与开销

| 部分 | 开销 | 说明 |
|---|---|---|
| Decal Volume Box | 每个脚印一次 DrawMesh | 比全屏绘制更节省像素计算 |
| Depth Sampling | 每个 Decal 像素采样一次深度 | 用于重建世界坐标 |
| Normal Reconstruction | 使用 ddx / ddy 近似重建 | 用于 Angle Fade |
| Multiple Decals | 每个 Decal 单独绘制 | 脚印数量越多 Draw Call 越多 |
| Fade Coroutine | 每个脚印一个 Coroutine | 当前数量较少时可接受 |

当前实现适合技术展示和小规模动态脚印效果。

如果需要大量脚印同时存在，后续可以考虑：

- Decal 数据批处理
- GPU Instancing
- 限制最大脚印数量
- 使用对象池减少 Instantiate / Destroy
- 使用 RenderTexture 累积地面痕迹

---

## 8. 当前限制

当前实现主要用于学习和技术展示，还不是完整商业级 Decal 系统。

已知限制：

- 当前每个脚印会生成一个独立 GameObject
- 当前使用 Coroutine 淡出和 Destroy，频繁生成时可能产生额外开销
- 当前 Decal 每个 Projector 单独 DrawMesh，脚印数量较多时 Draw Call 会增加
- 当前法线由深度近似重建，在边缘或深度不连续位置可能不稳定
- 当前脚印位置基于 `LeftFoot / RightFoot` 骨骼，脚掌中心需要通过 `footForwardOffset` 修正
- 当前走路和跑步共用同一套偏移参数，不同动画可能需要单独调整
- 当前没有实现对象池
- 当前没有实现最大脚印数量限制
- 当前没有做复杂地形材质判断，例如泥地、雪地、石地生成不同脚印
- 当前没有与角色 IK 系统结合

---

## 9. 后续改进计划

- 使用 LeftToes / RightToes 计算更准确的脚掌中心
- 为 Walk / Run 分别设置不同的 `footForwardOffset`
- 添加对象池，减少频繁 Instantiate / Destroy
- 添加最大脚印数量限制
- 添加脚印材质类型判断，例如泥地、雪地、沙地
- 添加脚印深浅变化，根据角色速度或地面材质控制 opacity
- 添加随机旋转、随机缩放，减少重复感
- 添加脚印 Debug Gizmos，显示 Raycast 和落脚点
- 支持更多 Decal 类型，例如血迹、弹孔、污渍
- 优化多 Decal 绘制，减少 Draw Call
- 尝试 RenderTexture 累积脚印，用于大规模地面痕迹系统

---

## 10. 小结

本模块完成了一个基于 URP14 的屏幕空间脚印 Decal 原型。

它结合了：

- URP Renderer Feature
- 自定义 Render Pass
- ShaderLab / HLSL
- Camera Depth Texture
- 世界坐标重建
- Decal Volume Box
- Animation Event
- Humanoid Foot Bone
- Runtime Decal Spawning
- Coroutine Fade

这个模块的重点不是单纯显示一个脚印贴图，而是从渲染管线、Shader 数据流和角色动画事件三个方向，完整串联出一个可运行的实时效果系统。

后续如果继续完善对象池、脚掌定位、材质判断和批处理优化，可以扩展成更完整的角色足迹 / 地面痕迹系统。

---


