# Unity URP14 Technical Art Shader Lab

这是一个基于 Unity URP14 的技术美术 Shader / Rendering 作品集项目。  
项目用于展示我在实时渲染、Shader 编写、URP Render Feature、自定义阴影、后处理和材质表现等方向的实践。

本项目不是简单调用 Unity 内置效果，而是尝试从底层原理出发，实现并分析常见实时渲染技术。

---

## Modules

| 模块 | 内容 | 状态 | 详细说明 |
|---|---|---|---|
| Custom Shadow Mapping | 硬阴影 / PCF / PCSS / 2 级 CSM | 已完成 | [查看文档](Docs/CustomShadow.md) |
| Dissolve Shader | 噪声溶解 / 边缘发光 | 计划中 | - |
| Outline Shader | 描边 / Rim Light | 计划中 | - |
| Water Shader | 水面 / Fresnel / 深度渐变 | 计划中 | - |
| Post Processing | 深度雾 / 屏幕后处理 | 计划中 | - |

---

## 当前重点展示：Custom Shadow Mapping

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

## 项目结构

```text
UnityURP14_TA_ShaderLab/
│
├── Assets/
│   └── ...
│
├── Docs/
│   └── CustomShadow.md
│
├── Packages/
├── ProjectSettings/
│
├── pictures/
│   └── shadows/
│       ├── hard_shadow.png
│       ├── pcf_shadow.png
│       ├── pcss_shadow.png
│       ├── csm_off.png
│       ├── csm_on.png
│       └── cascade_debug.png
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