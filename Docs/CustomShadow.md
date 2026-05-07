# Custom Shadow Mapping

本模块基于 Shadow Mapping 原理，在 Unity URP14 中实现了一套自定义阴影系统。

实现内容包括：

- 硬阴影
- PCF 软阴影
- PCSS 软阴影
- Normal Bias / Slope Depth Bias
- 2 级 CSM 级联阴影
- Cascade Debug 可视化
- LightUV / ReceiverDepth / DepthDifference 等调试模式
- 可调节的阴影参数面板

---

## 1. 实现目标

本模块的目标不是直接使用 Unity 内置阴影，而是从 Shadow Mapping 的底层流程出发，手动实现一套完整的自定义阴影流程。

整体流程：

```text
Light Camera
    ↓
Custom Render Feature
    ↓
R32_SFloat Depth Texture
    ↓
Receiver Shader
    ↓
World Position → Light UV
    ↓
Depth Compare
    ↓
PCF / PCSS / CSM
    ↓
Final Shadow
```

本实现重点关注以下问题：

- 如何在 URP 中通过 `ScriptableRendererFeature` 生成自定义 Shadow Map
- 如何在 Shader 中手动进行深度比较
- 如何通过 PCF / PCSS 改善阴影边缘
- 如何用多台 Light Camera 实现 2 级 CSM
- 如何定位 CSM、PCSS、LightUV 边界等常见问题

---

## 2. 功能列表

- 使用 `ScriptableRendererFeature` 自定义渲染深度图
- 使用 Light Camera 从光源视角渲染 Shadow Caster
- 使用 `R32_SFloat` 纹理保存线性深度
- 在接收阴影 Shader 中进行世界坐标到光源 UV 的转换
- 实现基础硬阴影深度比较
- 实现 5x5 Tent PCF 软阴影
- 实现简化版 PCSS 软阴影
- 实现 Normal Bias 和 Slope Depth Bias，减少 Shadow Acne
- 实现 2 级 CSM 级联阴影
- 使用主相机 View Space Depth 进行 Cascade 选择
- 支持 Cascade Debug 可视化
- 支持 LightUV / ReceiverDepth / DepthDifference 等调试模式
- 暴露参数供美术调节

---

## 3. 效果展示

### 3.1 硬阴影

基础 Shadow Map 深度比较。
每个像素只进行一次 Shadow Map 采样，因此阴影边缘锐利，但容易出现锯齿。

特点：

- 每像素 1 次采样
- 阴影边缘清晰
- 容易出现锯齿
- 作为 PCF / PCSS 的基础实现

---

### 3.2 PCF 软阴影

PCF 通过在 Shadow Map 周围区域进行多次采样，并对结果进行加权平均，从而柔化阴影边缘。

本项目使用 `5x5 Tent Filter`。

特点：

- 每像素 25 次采样
- 阴影边缘更平滑
- 支持调节 `PCF Radius`
- 性能开销高于硬阴影
- 稳定性通常高于 PCSS

---

### 3.3 PCSS 软阴影

PCSS 根据遮挡物和接收面的距离估算半影大小，实现更接近真实光照的软阴影效果。

实现流程：

```text
Blocker Search
    ↓
Penumbra Estimation
    ↓
Dynamic PCF Filtering
```

特点：

- 接触处阴影较硬
- 离遮挡物越远，阴影越软
- 效果比普通 PCF 更自然
- 采样次数更多，性能开销更高
- 对 Shadow Map 边界、Caster Layer、Bias 参数更敏感

---

### 3.4 CSM 级联阴影

单张 Shadow Map 如果覆盖很大范围，会导致近处阴影精度不足。
CSM 的思路是将主相机视野按深度分成多个区域，不同区域使用不同范围的 Shadow Map。

本项目实现了 2 级 CSM：

- Cascade 0：近处，小范围，高精度
- Cascade 1：远处，大范围，低精度

#### CSM 关闭

关闭 CSM 时，整个场景使用大范围 Shadow Map。
由于 Shadow Map 覆盖范围较大，近处阴影精度会下降。

#### CSM 打开

打开 CSM 后，近处区域使用小范围 Shadow Map，阴影精度更高；远处区域继续使用大范围 Shadow Map。

#### Cascade Debug

为了验证级联选择是否正确，Shader 提供了 Cascade Debug 模式。

- 红色：Cascade 0，近处高精度阴影
- 绿色：Cascade 1，远处大范围阴影

---

## 4. 技术实现

### 4.1 自定义深度图生成

使用 URP 的 `ScriptableRendererFeature` 插入自定义 Render Pass。
在 Light Camera 渲染时，将指定 Layer 的物体使用 Depth Material 重新绘制到自定义 Render Texture 中。

深度图格式使用 `R32_SFloat`。
这张纹理只使用 R 通道保存光源视角下的线性深度。

需要注意：

- 颜色 RT 保存可采样的线性深度
- 深度缓冲 RT 只用于 GPU 的 ZTest / ZWrite
- 自定义 Shadow Map 通常不需要 MSAA
- Shadow Map 推荐使用 `Clamp`，避免 UV 边界外采样导致重复纹理
- Caster Layer 应该只包含真正投射阴影的物体

在本项目中，Renderer Feature 通过 `casterLayerMask` 和 `excludeLayerMask` 控制哪些物体会写入自定义 Shadow Map。
推荐配置为：

```text
Sphere / Character / Dynamic Object → ShadowCaster Layer
Ground / Plane / Receiver Only      → Ground Layer

Caster Layer Mask  = ShadowCaster
Exclude Layer Mask = Ground
```

如果 Ground / Plane 被写入 Shadow Map，接收面会拿自己的深度和自己的深度做比较，容易产生大面积 Shadow Acne。PCSS 会进一步放大这种噪声。

---

### 4.2 深度写入

在深度 Shader 中，将物体顶点转换到光源相机 View Space，然后将 view depth 线性归一化到 0 到 1。

```hlsl
float viewDepth = -positionVS.z;
float linear01Depth = saturate((viewDepth - nearPlane) / (farPlane - nearPlane));
```

这样得到的深度图可以在接收阴影的 Shader 中进行手动深度比较。

---

### 4.3 阴影接收

接收阴影的物体在 Fragment Shader 中，将自身世界坐标转换到光源 UV 空间：

```hlsl
float4 lightUVH = mul(_WorldToLightUVMatrix, float4(positionWS, 1.0));
float2 lightUV = lightUVH.xy / lightUVH.w;
```

然后采样 Shadow Map：

```hlsl
float sampledDepth = SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, lightUV).r;
```

最后比较当前像素的深度和 Shadow Map 中记录的深度：

```hlsl
float shadow = receiverDepth > sampledDepth + bias ? 1.0 : 0.0;
```

如果当前像素在光源视角下比 Shadow Map 中记录的深度更远，说明光线先被其他物体遮挡，因此当前像素处于阴影中。

---

### 4.4 Bias 处理

Shadow Mapping 容易出现自阴影噪点，也就是 Shadow Acne。
本项目使用多种 Bias 减少这个问题：

- Depth Bias
- Normal Bias
- Slope Depth Bias

其中 Normal Bias 会把接收阴影的位置沿法线方向稍微推出去：

```hlsl
float3 receiverPositionWS = input.positionWS + normalWS * _NormalBias;
```

Slope Bias 根据表面和光照方向的夹角增加偏移量，减少掠射角下的阴影噪点：

```hlsl
float slopeBias = _SlopeDepthBias * (1.0 - NoL);
float totalBias = _DepthBias + slopeBias;
```

Bias 太小容易出现 Shadow Acne，Bias 太大则可能导致阴影和物体分离，也就是 Peter Panning。

在 CSM 中，推荐使用原始世界坐标判断 Cascade，使用 Normal Bias 后的位置做 Shadow Map 采样：

```hlsl
float3 originalPositionWS = input.positionWS;
float3 receiverPositionWS = input.positionWS + normalWS * _NormalBias;
```

这样可以避免 Cascade 边界因为 Normal Bias 发生轻微抖动。

---

### 4.5 PCF 实现

PCF 通过多次采样周围 shadow map texel，将多个硬阴影比较结果平均，从而得到更柔和的阴影边缘。

本项目使用 `5x5 Tent Filter`：

```hlsl
for (int y = -2; y <= 2; y++)
{
    for (int x = -2; x <= 2; x++)
    {
        float wx = 3.0 - abs((float)x);
        float wy = 3.0 - abs((float)y);
        float weight = wx * wy;

        float2 sampleUV = uv + texelSize * float2(x, y);
        shadow += SampleShadowTap(sampleUV, receiverDepth, bias) * weight;
        weightSum += weight;
    }
}
```

PCF 并不是物理正确的软阴影，而是通过滤波降低 Shadow Map 锯齿，让阴影边缘更平滑。

---

### 4.6 PCSS 实现

PCSS 用于模拟更自然的软阴影效果。
它的核心目标是实现接触处较硬、远离遮挡物时更软的阴影。

PCSS 分为三个阶段：

```text
Blocker Search
    ↓
Penumbra Estimation
    ↓
Dynamic PCF Filtering
```

首先搜索遮挡物 blocker：

```hlsl
if (sampledDepth < 0.999 && sampledDepth + bias < receiverDepth)
{
    blockerDepthSum += sampledDepth;
    blockerCount += 1.0;
}
```

然后根据 blocker 和 receiver 的距离估算半影大小：

```hlsl
float blockerViewDepth = lerp(nearPlane, farPlane, avgBlockerDepth01);
float penumbraRatio = max(receiverViewDepth - blockerViewDepth, 0.0) / max(blockerViewDepth, 0.001);
float filterRadius = penumbraRatio * _PCSSLightSize;
filterRadius = clamp(filterRadius, _PCFRadius, _PCSSMaxFilterRadius);
```

最后使用动态半径进行 PCF 滤波。

#### PCSS 当前注意点

PCSS 对以下问题非常敏感：

- Shadow Map UV 边界
- Ground 是否被写入 Shadow Map
- Bias 是否过小
- Blocker Search 半径是否过大
- Max Filter Radius 是否过大
- Shadow Map 分辨率是否不足

如果 `_PCSSLightSize`、`_PCSSBlockerSearchRadius`、`_PCSSMaxFilterRadius` 设置过大，PCSS 会把 Shadow Map 的边界、噪声和自阴影问题明显放大。

推荐调试初始值：

```text
PCF Radius                 = 1
PCSS Light Size            = 2 ~ 4
PCSS Blocker Search Radius = 1.5 ~ 2
PCSS Max Filter Radius     = 5 ~ 8
Depth Bias                 = 0.004 ~ 0.008
Normal Bias                = 0.02 ~ 0.05
Slope Depth Bias           = 0.001 ~ 0.002
```

---

### 4.7 CSM 实现

CSM 使用两台 Light Camera 分别生成两张 Shadow Map：

```text
LightCamera_Cascade0 → _MyCustomDepthTexture0
LightCamera_Cascade1 → _MyCustomDepthTexture1
```

对应两套矩阵：

```text
_WorldToLightUVMatrix0
_WorldToLightUVMatrix1
_WorldToLightViewMatrix0
_WorldToLightViewMatrix1
```

以及两套深度参数：

```text
_CustomLightDepthParams0
_CustomLightDepthParams1
```

当前实现为 2 级 CSM：

- Cascade 0：近处，小范围，高精度
- Cascade 1：远处，大范围，低精度

#### 4.7.1 Cascade 选择：从世界距离改为主相机 View Space 深度

最初版本使用的是世界空间距离：

```hlsl
float cameraDistance = distance(positionWS, _WorldSpaceCameraPos);
int cascadeIndex = cameraDistance < _CascadeSplit0 ? 0 : 1;
```

这个做法的含义是：

```text
以主相机位置为球心，半径小于 split 的区域使用 Cascade 0。
```

但是 CSM 更常见的切分方式是沿主相机视锥体深度方向切分，而不是按球形距离切分。
因此后续改为使用主相机 View Space 深度：

```hlsl
int SelectCascadeIndex(float3 positionWS)
{
    if (_UseCSM < 0.5)
    {
        return 1;
    }

    float3 viewPos = TransformWorldToView(positionWS);
    float cameraViewDepth = -viewPos.z;

    return cameraViewDepth < _CascadeSplit0 ? 0 : 1;
}
```

这样 `_CascadeSplit0` 的含义变为：

```text
主相机前方多少距离以内使用 Cascade 0。
```

同时，最大阴影距离也应该使用相机空间深度：

```hlsl
float3 mainCameraViewPos = TransformWorldToView(input.positionWS);
float mainCameraViewDepth = -mainCameraViewPos.z;

if (mainCameraViewDepth > _CascadeMaxDistance)
{
    return float4(unshadowedColor, _BaseColor.a);
}
```

#### 4.7.2 Cascade 相机布局

Directional Light 的 position 对方向光本身并不重要，真正影响阴影方向的是 rotation。
因此，级联阴影相机更推荐按“各自看向中心”来布局，而不是简单跟随 Light 的位置。

推荐思路：

```csharp
camera.rotation = light.rotation;
camera.position = cascadeCenter - light.forward * distanceFromCenter;
```

也就是说：

- Light 负责提供方向
- Cascade 0 有自己的近处中心点
- Cascade 1 有自己的远处或整体场景中心点
- 每个 Cascade Camera 围绕自己的中心点旋转

推荐场景结构：

```text
Directional Light
    DirectionalLightOrbit.cs

LightCamera_Cascade0
    CascadeShadowCameraFollower.cs
    Look Center = Cascade0_Center

LightCamera_Cascade1
    CascadeShadowCameraFollower.cs
    Look Center = Cascade1_Center
```

Cascade 0 可以只覆盖近处几个物体，Cascade 1 可以覆盖整个场景。二者的 Orthographic Size、Near/Far、Center 和 Distance 都可以独立调节。

---

## 5. 可调参数

主要参数暴露在材质面板中，方便调节阴影效果。

| 参数 | 作用 |
| --- | --- |
| `Shadow Strength` | 控制阴影强度 |
| `Depth Bias` | 控制基础深度偏移，减少自阴影噪点 |
| `Normal Bias` | 沿法线方向偏移接收点，减少 Shadow Acne |
| `Slope Depth Bias` | 根据表面角度增加 Bias，减少掠射角噪点 |
| `PCF Radius` | 控制 PCF 采样半径 |
| `Use PCF` | 是否启用 PCF |
| `Use PCSS` | 是否启用 PCSS |
| `PCSS Light Size` | 模拟光源尺寸，影响软阴影范围 |
| `PCSS Blocker Search Radius` | 控制 blocker 搜索范围 |
| `PCSS Max Filter Radius` | 限制 PCSS 最大滤波半径 |
| `Use Cascade Shadow` | 是否启用 CSM |
| `Cascade Split` | 控制 Cascade 0 和 Cascade 1 的主相机 View Space 分界距离 |
| `Cascade Max Distance` | 超过该主相机 View Space 距离后不再计算自定义阴影 |
| `Debug Mode` | 切换不同调试显示模式 |

---

## 6. Debug 模式

为了方便调试，Shader 提供了多种 Debug 模式。

| Debug 模式 | 作用 |
| --- | --- |
| `SampledDepth` | 显示 Shadow Map 中采样到的深度 |
| `LightUV` | 显示当前像素映射到 Shadow Map 后的 UV |
| `ReceiverDepth` | 显示当前像素在光源空间中的线性深度 |
| `DepthDifference` | 显示 receiver depth 和 sampled depth 的差值 |
| `ShadowCompare` | 显示最终阴影比较结果 |
| `CascadeIndex` | 显示当前像素使用的 Cascade |

这些 Debug 模式用于检查阴影错误来源，例如 UV 是否正确、深度是否匹配、Cascade 选择是否正确等。

### 6.1 常见 Debug 判断流程

#### 判断是否是 Cascade 边界

切换到 `CascadeIndex`：

- 如果问题边界正好是红 / 绿交界，则说明是 Cascade 分割边界。
- 如果问题边界和红 / 绿交界无关，则不是 Cascade 边界。

Cascade 边界问题通常需要 Cascade Blending 解决。

#### 判断是否是 LightUV 边界

切换到 `LightUV`：

- 如果问题边界正好对应 LightUV 的 0 或 1 边缘，则说明当前像素已经接近 Light Camera 的正交范围边界。
- 这种线通常会跟随 Light Camera 或光源方向旋转。
- PCSS 比 PCF 更容易暴露这个问题。

解决方向：

```text
增大 LightCamera 的 Orthographic Size
调整 Cascade Center，让场景落在 LightUV 中间
降低 PCSS 采样半径
添加 ShadowMap 边缘淡出
```

#### 判断是否是自阴影噪声

切换到 `DepthDifference` 或 `ShadowCompare`：

- 如果大面积地面出现密集条纹或噪点，优先检查 Ground 是否被写入 Shadow Map。
- 如果只有掠射角区域噪声明显，优先调整 Normal Bias / Slope Bias。
- 如果只在 PCSS 打开后噪声明显，优先降低 PCSS 参数或增加 blocker count 过滤。

---

## 7. 本次调试中遇到的问题记录

### 7.1 多个 Cascade 相机不应该全部塞进 Light 脚本

最初尝试是在 Directional Light 上挂多个相机设置，统一管理 Cascade0 / Cascade1。
这样虽然能工作，但脚本会越来越臃肿。

更清晰的拆分方式是：

```text
Directional Light:
    只负责旋转光源方向

Each Light Camera:
    只负责根据 Light 方向和自己的 Center 更新位置与旋转
```

这样每个 Cascade Camera 独立配置：

- Look Center
- Distance From Center
- Orthographic Size
- Near / Far Clip Plane
- Rotation Offset

扩展到 3 级或 4 级 CSM 时，只需要新增 Light Camera 和对应 follower 脚本，不需要继续膨胀 Light 管理脚本。

---

### 7.2 Light 旋转时，Cascade Camera 应该围绕自己的中心点旋转

错误理解：

```text
相机离 Light 有一个 offset，所以相机应该围绕 Light 或 offset 点旋转。
```

更合理的理解：

```text
每个 Cascade Camera 都有自己的 Shadow Coverage Center。
Light 只提供方向。
Camera 根据 Light 的方向站到 Center 的反方向。
```

核心公式：

```csharp
Quaternion finalRotation = directionalLight.rotation * Quaternion.Euler(rotationOffsetEuler);
Vector3 forwardDirection = finalRotation * Vector3.forward;

Vector3 cameraPosition = center - forwardDirection.normalized * distanceFromCenter;
transform.SetPositionAndRotation(cameraPosition, finalRotation);
```

含义：

- `finalRotation`：当前阴影相机最终使用的旋转
- `forwardDirection`：把本地 `(0,0,1)` 转成世界空间方向
- `cameraPosition`：把相机放在中心点的反方向，使它朝向中心点

这样 Light 旋转时，Cascade0 会围绕 Cascade0_Center 转，Cascade1 会围绕 Cascade1_Center 转。

---

### 7.3 Cascade 判断从世界距离改成相机空间深度

最初使用：

```hlsl
float cameraDistance = distance(positionWS, _WorldSpaceCameraPos);
```

这个是世界空间直线距离，会形成球形切分。

后续改为：

```hlsl
float3 viewPos = TransformWorldToView(positionWS);
float cameraViewDepth = -viewPos.z;
```

这个是主相机 View Space 深度，更符合 CSM 沿视锥深度切分的思路。

同时，`_CascadeMaxDistance` 也应使用同一种深度标准，避免 split 和 max distance 的含义不一致。

---

### 7.4 PCSS 下出现跟随相机旋转的横线

现象：

- 开启 PCSS 后，地面上出现一条明显边界线
- 该边界会跟随 Light Camera 或光源方向旋转
- DebugMode 切到 `CascadeIndex` 后发现不是红绿交界
- DebugMode 切到 `LightUV` 后发现边界对应 LightUV 边界

结论：

```text
这不是 Cascade 分割线，而是 LightCamera 正交范围 / ShadowMap UV 边界。
```

原因：

- 当前像素接近或超出 Shadow Map 的 UV 范围
- PCSS 的 blocker search 和动态 PCF 会采样更大的范围
- 边界处采样结果突然变化，因此形成明显线条

解决方向：

1. 增大对应 Cascade LightCamera 的 `Orthographic Size`
2. 调整对应 `Cascade Center`，让场景主体落在 LightUV 中央
3. 降低 `_PCSSBlockerSearchRadius` 和 `_PCSSMaxFilterRadius`
4. 对 ShadowMap 边缘做 fade out
5. 不要把 UV outside 作为过早的硬切条件，而是让边缘阴影逐渐减弱

示例边缘淡出：

```hlsl
float GetShadowMapEdgeFadeUV(float2 uv, float fadeWidth)
{
    float distToEdge = min(
        min(uv.x, 1.0 - uv.x),
        min(uv.y, 1.0 - uv.y)
    );

    return smoothstep(0.0, fadeWidth, distToEdge);
}
```

使用方式：

```hlsl
float edgeFade = GetShadowMapEdgeFadeUV(lightUV, _ShadowEdgeFadeUV);
shadow *= edgeFade;
```

需要注意：如果只计算了 `edgeFade`，但没有真正执行 `shadow *= edgeFade`，边缘淡出不会生效。

---

### 7.5 PCSS 噪声明显

现象：

- 开启 PCSS 后，地面出现大量条纹 / 波纹 / 噪声
- PCF 下问题不明显，PCSS 下明显放大

可能原因：

1. Ground / Plane 被写入了 Shadow Map
2. Bias 太小，导致接收面和自身深度比较
3. PCSS blocker search 半径过大
4. PCSS max filter radius 过大
5. Shadow Map 分辨率不足
6. LightCamera Orthographic Size 太大，导致单位世界空间 texel 精度不足

优先排查顺序：

```text
1. 确认 Ground 不在 Caster Layer 中
2. 确认 Exclude Layer Mask 排除了 Ground
3. 降低 PCSS 参数
4. 增加 Depth Bias / Normal Bias / Slope Depth Bias
5. 提高 Shadow Map 分辨率
6. 缩小对应 Cascade 的 Orthographic Size
```

推荐 PCSS 稳定性策略：

```hlsl
float pcfFallback = SampleShadowPCF(cascadeIndex, uv, receiverDepth, bias);

if (blockerCount < _PCSSMinBlockerCount)
{
    return pcfFallback;
}
```

当 blocker 数量太少时，PCSS 的平均 blocker 深度会非常不稳定。此时退回 PCF 可以减少闪烁和噪声。

---

## 8. 性能与采样成本

| 模式 | 大致采样成本 | 说明 |
| --- | --- | --- |
| Hard Shadow | 1 次 shadow map 采样 | 最便宜，但边缘锯齿明显 |
| PCF 5x5 | 25 次 shadow map 采样 | 边缘更平滑，性能开销更高 |
| PCSS | 约 50 次 shadow map 采样 | 包含 blocker search 和动态 PCF，效果更自然但更不稳定 |
| 2 级 CSM | 额外渲染一张 shadow map | 提升近处精度，但增加一次 Light Camera 渲染 |

PCSS 的实际成本取决于：

- blocker search 采样数量
- PCF filter 采样数量
- 是否采样多个 cascade
- 是否做 cascade blending
- 是否加入随机旋转核或 Poisson Disk

当前实现用于学习和效果验证，暂未做复杂采样优化。

---

## 9. 当前限制

当前实现主要用于学习和技术展示，还不是完整商业级阴影系统。

已知限制：

- 暂未实现 Cascade 之间的平滑过渡
- 每个 Cascade 使用独立 Shadow Map，暂未打包进 Shadow Atlas
- 暂未实现 Texel Snapping，相机或光源移动时可能出现轻微阴影抖动
- 当前光照模型是简化版漫反射，并未完整集成 URP Lit / PBR
- PCSS 采样次数较高，性能开销较大
- PCSS 对 ShadowMap 边界和 Caster Layer 设置比较敏感
- ShadowMap 边缘淡出目前只是调试思路，仍需要继续完善
- 暂未实现 PCSS 的稳定采样核、旋转采样或时间稳定性处理
- 暂未实现 Cascade Blending，因此级联切换处仍可能出现硬边界

---

## 10. 后续改进计划

- 添加 Cascade Blending，减少级联切换边界
- 将多张 Cascade Shadow Map 打包到 Shadow Atlas
- 添加 Texel Snapping，减少阴影闪烁
- 完善 ShadowMap 边缘淡出，避免 LightUV 边界产生硬线
- 优化 PCSS blocker search，加入最小 blocker 数量判断
- 尝试 Poisson Disk / Blue Noise 采样核，降低 PCSS 规则条纹
- 增加 PCSS fallback，当不稳定时退回 PCF
- 集成到 URP Lit Shader
- 添加运行时 Debug Overlay
- 添加性能统计和采样次数显示
- 扩展更多 Shader / Rendering 示例
