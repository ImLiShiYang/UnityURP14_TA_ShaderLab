# Unity URP14 Technical Art Shader Lab

这是一个基于 Unity URP14 的技术美术 Shader / Rendering 作品集项目。  
项目用于展示我在实时渲染、Shader 编写、URP Render Feature、自定义阴影、屏幕空间贴花、后处理和材质表现等方向的实践。

本项目不是简单调用 Unity 内置效果，而是尝试从底层原理出发，实现并分析常见实时渲染技术。

---

## Modules

| 模块 | 内容 | 状态 | 详细说明 |
|---|---|---|---|
| Custom Shadow Mapping | 硬阴影 / PCF / PCSS / 2 级 CSM | 已完成 | [查看文档](Docs/CustomShadow.md) |
| Screen Space Footprint Decal | 屏幕空间脚印贴花 / 体积盒 Decal / 动画事件生成脚印 | 初步完成 | [查看文档](Docs/ScreenSpaceFootprints.md) |
| Dissolve Shader | 噪声溶解 / 边缘发光 | 计划中 | - |
| Outline Shader | 描边 / Rim Light | 计划中 | - |
| Water Shader | 水面 / Fresnel / 深度渐变 | 计划中 | - |
| Post Processing | 深度雾 / 屏幕后处理 | 计划中 | - |

---

## 当前重点展示

### Custom Shadow Mapping

本模块基于 Shadow Mapping 原理，在 URP14 中实现了一套自定义阴影系统。

主要内容包括：

- 自定义 `ScriptableRendererFeature` 生成深度图
- 使用 `R32_SFloat` RenderTexture 保存光源视角线性深度
- 基础硬阴影深度比较
- 5x5 Tent PCF 软阴影
- 简化版 PCSS 软阴影
- Normal Bias / Slope Depth Bias
- 2 级 CSM 级联阴影
- Cascade Debug 可视化
- 可调节的阴影参数面板

![CSM Demo](pictures/shadows/csm_on.png)

详细技术说明见：[Docs/CustomShadow.md](Docs/CustomShadow.md)

---

### Screen Space Footprint Decal

本模块基于屏幕空间 Decal 思路，在 URP14 中实现了一套运行时脚印贴花系统。

系统不是直接使用 Unity 内置 Decal Projector，而是通过自定义 `ScriptableRendererFeature`、体积盒绘制、深度重建世界坐标和动画事件，在角色落脚位置生成脚印。

主要内容包括：

- 自定义 Screen Space Decal Renderer Feature
- 使用体积盒 Cube 绘制 Decal 投射范围
- 通过 `_CameraDepthTexture` 重建世界坐标
- 在 Shader 中判断像素是否位于 Decal 投射盒内部
- 支持贴花纹理、颜色、透明度、边缘淡出、距离淡出
- 支持多个 Decal Projector 同时绘制
- 基于 Humanoid 左右脚骨骼生成脚印
- 使用 Animation Event 在 Walking / Running 落脚帧触发脚印
- 支持左右脚不同贴图
- 支持脚印生命周期与淡出销毁

<!-- 后续添加效果图后取消注释 -->
<!-- ![Footprint Demo](pictures/footprints/footprint_runtime.png) -->

详细技术说明见：[Docs/ScreenSpaceFootprints.md](Docs/ScreenSpaceFootprints.md)

---

## 项目结构

```text
UnityURP14_TA_ShaderLab/
│
├── Assets/
│   └── ...
│
├── Docs/
│   ├── CustomShadow.md
│   └── ScreenSpaceFootprints.md
│
├── Packages/
├── ProjectSettings/
│
├── pictures/
│   ├── shadows/
│   │   ├── hard_shadow.png
│   │   ├── pcf_shadow.png
│   │   ├── pcss_shadow.png
│   │   ├── csm_off.png
│   │   ├── csm_on.png
│   │   └── cascade_debug.png
│   │
│   └── footprints/
│       ├── footprint_runtime.png
│       ├── footprint_decal_box.png
│       └── footprint_fade.png
│
├── README.md
└── .gitignore
```

---

## 项目环境

| 项目 | 说明 |
|---|---|
| Unity | URP 14 |
| Shader | HLSL / ShaderLab |
| Script | C# |
| Render Pipeline | Universal Render Pipeline |