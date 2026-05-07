# Custom Shadow Mapping

本模块基于 Shadow Mapping 原理，在 Unity URP14 中实现了一套自定义阴影系统。

实现内容包括：

- 硬阴影
- PCF 软阴影
- PCSS 软阴影
- Normal Bias / Slope Depth Bias
- 2 级 CSM 级联阴影
- Cascade Debug 可视化
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
- 支持 Cascade Debug 可视化
- 暴露参数供美术调节

---

## 3. 效果展示

### 3.1 硬阴影

基础 Shadow Map 深度比较。  
每个像素只进行一次 Shadow Map 采样，因此阴影边缘锐利，但容易出现锯齿。

![Hard Shadow](../pictures/shadows/hard_shadow.png)

特点：

- 每像素 1 次采样
- 阴影边缘清晰
- 容易出现锯齿
- 作为 PCF / PCSS 的基础实现

---

### 3.2 PCF 软阴影

PCF 通过在 Shadow Map 周围区域进行多次采样，并对结果进行加权平均，从而柔化阴影边缘。

![PCF Shadow](../pictures/shadows/pcf_shadow.png)

本项目使用 `5x5 Tent Filter`。

特点：

- 每像素 25 次采样
- 阴影边缘更平滑
- 支持调节 `PCF Radius`
- 性能开销高于硬阴影

---

### 3.3 PCSS 软阴影

PCSS 根据遮挡物和接收面的距离估算半影大小，实现更接近真实光照的软阴影效果。

![PCSS Shadow](../pictures/shadows/pcss_shadow.png)

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

---

### 3.4 CSM 级联阴影

单张 Shadow Map 如果覆盖很大范围，会导致近处阴影精度不足。  
CSM 的思路是将相机视野按距离分成多个区域，不同区域使用不同范围的 Shadow Map。

本项目实现了 2 级 CSM：

- Cascade 0：近处，小范围，高精度
- Cascade 1：远处，大范围，低精度

#### CSM 关闭

关闭 CSM 时，整个场景使用大范围 Shadow Map。  
由于 Shadow Map 覆盖范围较大，近处阴影精度会下降。

![CSM Off](../pictures/shadows/csm_off.png)

#### CSM 打开

打开 CSM 后，近处区域使用小范围 Shadow Map，阴影精度更高；远处区域继续使用大范围 Shadow Map。

![CSM On](../pictures/shadows/csm_on.png)

#### Cascade Debug

为了验证级联选择是否正确，Shader 提供了 Cascade Debug 模式。

- 红色：Cascade 0，近处高精度阴影
- 绿色：Cascade 1，远处大范围阴影

![Cascade Debug](../pictures/shadows/cascade_debug.png)

---

## 4. 技术实现

### 4.1 自定义深度图生成

使用 URP 的 `ScriptableRendererFeature` 插入自定义 Render Pass。  
在 Light Camera 渲染时，将指定 Layer 的物体使用 Depth Material 重新绘制到自定义 Render Texture 中。

深度图格式使用 `R32_SFloat`。  
这张纹理只使用 R 通道保存光源视角下的线性深度。

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

---

### 4.5 PCF 实现

PCF 通过多次采样周围 shadow map texel，将多个硬阴影比较结果平均，从而得到更柔和的阴影边缘。

本项目使用 `5x5 Tent Filter`：

```hlsl
for (int y = -2; y <= 2; y++)
{
    for (int x = -2; x <= 2; x++)
    {
        float2 sampleUV = uv + texelSize * float2(x, y);
        shadow += SampleShadowTap(sampleUV, receiverDepth, bias) * weight;
    }
}
```

PCF 并不是物理正确的软阴影，而是通过滤波降低 shadow map 锯齿，让阴影边缘更平滑。

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
if (sampledDepth + bias < receiverDepth)
{
    blockerDepthSum += sampledDepth;
    blockerCount += 1.0;
}
```

然后根据 blocker 和 receiver 的距离估算半影大小：

```hlsl
float penumbraRatio = (receiverViewDepth - blockerViewDepth) / blockerViewDepth;
float filterRadius = penumbraRatio * _PCSSLightSize;
```

最后使用动态半径进行 PCF 滤波。

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
```

Shader 根据当前像素到主相机的距离选择 Cascade：

```hlsl
float cameraDistance = distance(positionWS, _WorldSpaceCameraPos);
int cascadeIndex = cameraDistance < _CascadeSplit0 ? 0 : 1;
```

然后根据 `cascadeIndex` 选择对应的 Shadow Map、矩阵和深度参数。

当前实现为 2 级 CSM：

- Cascade 0：近处，小范围，高精度
- Cascade 1：远处，大范围，低精度

---

## 5. 可调参数

主要参数暴露在材质面板中，方便调节阴影效果。

| 参数 | 作用 |
|---|---|
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
| `Cascade Split` | 控制 Cascade 0 和 Cascade 1 的分界距离 |
| `Cascade Max Distance` | 超过该距离后不再计算自定义阴影 |
| `Debug Mode` | 切换不同调试显示模式 |

---

## 6. Debug 模式

为了方便调试，Shader 提供了多种 Debug 模式。

| Debug 模式 | 作用 |
|---|---|
| `SampledDepth` | 显示 Shadow Map 中采样到的深度 |
| `LightUV` | 显示光源 UV |
| `ReceiverDepth` | 显示当前像素在光源空间中的深度 |
| `DepthDifference` | 显示 receiver depth 和 sampled depth 的差值 |
| `ShadowCompare` | 显示最终阴影比较结果 |
| `CascadeIndex` | 显示当前像素使用的 Cascade |

这些 Debug 模式用于检查阴影错误来源，例如 UV 是否正确、深度是否匹配、Cascade 选择是否正确等。

---

## 7. 性能与采样成本

| 模式 | 大致采样成本 | 说明 |
|---|---|---|
| Hard Shadow | 1 次 shadow map 采样 | 最便宜，但边缘锯齿明显 |
| PCF 5x5 | 25 次 shadow map 采样 | 边缘更平滑，性能开销更高 |
| PCSS | 约 50 次 shadow map 采样 | 包含 blocker search 和动态 PCF |
| 2 级 CSM | 额外渲染一张 shadow map | 提升近处精度，但增加一次 Light Camera 渲染 |

---

## 8. 当前限制

当前实现主要用于学习和技术展示，还不是完整商业级阴影系统。

已知限制：

- 暂未实现 Cascade 之间的平滑过渡
- 每个 Cascade 使用独立 Shadow Map，暂未打包进 Shadow Atlas
- 暂未实现 Texel Snapping，相机或光源移动时可能出现轻微阴影抖动
- 当前光照模型是简化版漫反射，并未完整集成 URP Lit / PBR
- PCSS 采样次数较高，性能开销较大

---

## 9. 后续改进计划

- 添加 Cascade Blending，减少级联切换边界
- 将多张 Cascade Shadow Map 打包到 Shadow Atlas
- 添加 Texel Snapping，减少阴影闪烁
- 集成到 URP Lit Shader
- 添加运行时 Debug Overlay
- 添加性能统计和采样次数显示
- 扩展更多 Shader / Rendering 示例