import argparse
import math
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


INF = 1.0e20


def edt_1d(values):
    n = values.shape[0]
    result = np.empty(n, dtype=np.float32)
    v = np.zeros(n, dtype=np.int32)
    z = np.empty(n + 1, dtype=np.float32)
    k = 0
    v[0] = 0
    z[0] = -INF
    z[1] = INF

    for q in range(1, n):
        while True:
            r = v[k]
            s = ((values[q] + q * q) - (values[r] + r * r)) / (2.0 * q - 2.0 * r)
            if s > z[k]:
                break
            k -= 1
        k += 1
        v[k] = q
        z[k] = s
        z[k + 1] = INF

    k = 0
    for q in range(n):
        while z[k + 1] < q:
            k += 1
        d = q - v[k]
        result[q] = d * d + values[v[k]]

    return result


def distance_to(mask):
    h, w = mask.shape
    source = np.where(mask, 0.0, INF).astype(np.float32)
    temp = np.empty_like(source)
    out = np.empty_like(source)

    for y in range(h):
        temp[y, :] = edt_1d(source[y, :])
    for x in range(w):
        out[:, x] = edt_1d(temp[:, x])

    return np.sqrt(out)


def signed_distance(lit_mask):
    return distance_to(~lit_mask) - distance_to(lit_mask)


def load_binary_masks(mask_paths, enforce_monotonic=True):
    masks = []
    cumulative = None
    for path in mask_paths:
        image = Image.open(path).convert("L")
        mask = np.asarray(image, dtype=np.uint8) > 127
        if enforce_monotonic:
            cumulative = mask if cumulative is None else np.logical_or(cumulative, mask)
            mask = cumulative
        masks.append(mask)

    if not masks:
        raise ValueError("No masks were supplied.")

    shape = masks[0].shape
    if any(mask.shape != shape for mask in masks):
        raise ValueError("All masks must have the same resolution.")

    return masks


def threshold_from_masks(masks):
    count = len(masks)
    signed = [signed_distance(mask) for mask in masks]
    stack = np.stack(masks, axis=0)
    h, w = masks[0].shape
    threshold = np.ones((h, w), dtype=np.float32)

    first_on = np.argmax(stack, axis=0)
    ever_on = np.any(stack, axis=0)
    threshold[stack[0]] = 0.0

    for index in range(1, count):
        transitioned = ever_on & (first_on == index)
        if not np.any(transitioned):
            continue

        prev_dist = signed[index - 1][transitioned]
        next_dist = signed[index][transitioned]
        denom = next_dist - prev_dist
        blend = np.where(np.abs(denom) > 1.0e-5, -prev_dist / denom, 0.5)
        blend = np.clip(blend, 0.0, 1.0)
        threshold[transitioned] = (index - 1 + blend) / (count - 1)

    return np.clip(threshold, 0.0, 1.0)


def save_luma(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.clip(data * 255.0 + 0.5, 0, 255).astype(np.uint8), mode="L").save(path)


def save_rgba(path, threshold, face_mask):
    path.parent.mkdir(parents=True, exist_ok=True)
    t = np.clip(threshold * 255.0 + 0.5, 0, 255).astype(np.uint8)
    m = np.clip(face_mask * 255.0 + 0.5, 0, 255).astype(np.uint8)
    b = np.zeros_like(t)
    a = m
    Image.fromarray(np.dstack([t, m, b, a]), mode="RGBA").save(path)


def save_rgba_from_luma(path, luma_image, face_mask):
    path.parent.mkdir(parents=True, exist_ok=True)
    luma = np.asarray(luma_image.convert("L").resize(face_mask.shape[::-1], Image.Resampling.BILINEAR), dtype=np.uint8)
    mask_image = Image.fromarray(np.where(face_mask, 255, 0).astype(np.uint8), mode="L")
    mask = np.asarray(mask_image.filter(ImageFilter.GaussianBlur(radius=1.25)), dtype=np.uint8)
    zero = np.zeros_like(luma)
    Image.fromarray(np.dstack([luma, mask, zero, mask]), mode="RGBA").save(path)


def save_preview(path, threshold, face_mask):
    path.parent.mkdir(parents=True, exist_ok=True)
    gray = np.clip(threshold * 255.0 + 0.5, 0, 255).astype(np.uint8)
    mask = np.clip(face_mask * 255.0 + 0.5, 0, 255).astype(np.uint8)
    preview = np.dstack([gray, gray, gray])
    preview[:, :, 1] = np.maximum(preview[:, :, 1], (mask * 0.45).astype(np.uint8))
    Image.fromarray(preview, mode="RGB").save(path)


def list_example_masks(example_dir):
    names = ["a.png", "b.png", "c.png", "d.png", "e.png", "f.png", "g.png"]
    paths = [example_dir / name for name in names]
    missing = [str(path) for path in paths if not path.exists()]
    if missing:
        raise FileNotFoundError("Missing example masks: " + ", ".join(missing))
    return paths


def compare_reference(output, reference):
    ref = np.asarray(Image.open(reference).convert("L"), dtype=np.float32) / 255.0
    if ref.shape != output.shape:
        ref = np.asarray(Image.open(reference).convert("L").resize(output.shape[::-1], Image.Resampling.BILINEAR), dtype=np.float32) / 255.0
    mse = float(np.mean((output - ref) ** 2))
    inv_mse = float(np.mean(((1.0 - output) - ref) ** 2))
    return mse, inv_mse


def load_or_build_face_mask(texture_dir, size):
    sdf_path = texture_dir / "face_SDF.png"
    if sdf_path.exists():
        image = Image.open(sdf_path).convert("RGBA").resize((size, size), Image.Resampling.BILINEAR)
        channel = np.asarray(image, dtype=np.uint8)[:, :, 1]
        mask = channel > 32
        if np.mean(mask) > 0.05:
            return mask

    h = w = size
    yy, xx = np.mgrid[0:h, 0:w]
    cx = w * 0.5
    cy = h * 0.52
    rx = w * 0.37
    ry = h * 0.36
    return ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1.0


def soften_mask_edges(mask, radius):
    if radius <= 0:
        return mask.astype(np.float32)
    image = Image.fromarray(np.where(mask, 255, 0).astype(np.uint8), mode="L")
    return np.asarray(image.filter(ImageFilter.GaussianBlur(radius=radius)), dtype=np.float32) / 255.0


def make_dessi_masks(texture_dir, output_dir, size=2048):
    face_mask = load_or_build_face_mask(texture_dir, size)
    ys, xs = np.where(face_mask)
    if xs.size == 0:
        raise ValueError("Dessi face mask is empty.")

    min_x, max_x = int(xs.min()), int(xs.max())
    min_y, max_y = int(ys.min()), int(ys.max())
    cx = size * 0.5
    cy = (min_y + max_y) * 0.5
    rx = max(1.0, max(abs(max_x - cx), abs(cx - min_x)))
    ry = max(1.0, (max_y - min_y) * 0.5)

    yy, xx = np.mgrid[0:size, 0:size]
    nx = (xx - cx) / rx
    ny = (yy - cy) / ry

    curvature = 0.18 * (ny + 0.05) ** 2 - 0.05 * ny
    sweep = nx - curvature

    nose_x = size * 0.5
    nose_y = cy - ry * 0.12
    nose_shadow = ((xx - (nose_x - rx * 0.018)) / (rx * 0.035)) ** 2 + ((yy - nose_y) / (ry * 0.055)) ** 2 < 1.0
    nose_light = ((xx - (nose_x + rx * 0.025)) / (rx * 0.025)) ** 2 + ((yy - (nose_y - ry * 0.015)) / (ry * 0.04)) ** 2 < 1.0

    thresholds = [0.78, 0.46, 0.24, 0.02, -0.18, -0.42, -0.72]
    masks = []
    cumulative = np.zeros_like(face_mask, dtype=bool)
    output_dir.mkdir(parents=True, exist_ok=True)

    for i, threshold in enumerate(thresholds):
        lit = face_mask & (sweep > threshold)
        if i <= 2:
            lit |= face_mask & nose_light
        lit &= ~(face_mask & nose_shadow)
        cumulative |= lit
        cumulative &= face_mask
        masks.append(cumulative.copy())
        image = np.where(cumulative, 255, 0).astype(np.uint8)
        Image.fromarray(np.dstack([image, image, image]), mode="RGB").save(output_dir / f"dessi_{i + 1:02d}.png")

    return masks, face_mask


def build_example(args):
    paths = list_example_masks(Path(args.example_dir))
    out_dir = Path(args.output_dir)
    if args.planeb_exe:
        output = out_dir / "article_wow_planeb.png"
        run_planeb(Path(args.planeb_exe), Path(args.example_dir), [path.name for path in paths], output, args.blendingtimes)
        print(f"wrote {output}")
        return None

    masks = load_binary_masks(paths)
    threshold = threshold_from_masks(masks)
    save_luma(out_dir / "article_wow_rebuilt.png", threshold)
    save_luma(out_dir / "article_wow_rebuilt_invert.png", 1.0 - threshold)
    if args.reference:
        mse, inv_mse = compare_reference(threshold, Path(args.reference))
        print(f"article mse={mse:.6f} invert_mse={inv_mse:.6f}")
    return threshold


def run_planeb(exe_path, folder_path, names, export_path, blendingtimes):
    exe_path = exe_path.resolve()
    folder_path = folder_path.resolve()
    export_path = export_path.resolve()

    if not exe_path.exists():
        raise FileNotFoundError(str(exe_path))

    export_path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.check_call([
        str(exe_path),
        "--folderpath",
        str(folder_path) + "\\",
        "--name",
        " ".join(names),
        "--exportpath",
        str(export_path),
        "--blendingtimes",
        str(blendingtimes),
    ])


def build_dessi(args):
    texture_dir = Path(args.texture_dir)
    mask_dir = texture_dir / "FaceSDFMasks"
    masks, face_mask = make_dessi_masks(texture_dir, mask_dir, args.size)
    mask_names = [f"dessi_{i + 1:02d}.png" for i in range(len(masks))]

    if args.planeb_exe:
        raw_output = texture_dir / "face_SDF_from_planeb_raw.png"
        run_planeb(Path(args.planeb_exe), mask_dir, mask_names, raw_output, args.blendingtimes)
        raw = Image.open(raw_output).convert("L")
        if args.invert_dessi:
            raw = Image.fromarray(255 - np.asarray(raw, dtype=np.uint8), mode="L")
        save_rgba_from_luma(texture_dir / "face_SDF.png", raw, face_mask)
        save_rgba_from_luma(texture_dir / "face_SDF_from_article_masks.png", raw, face_mask)
        save_preview(texture_dir / "face_SDF_preview.png", np.asarray(raw, dtype=np.float32) / 255.0, face_mask.astype(np.float32))
        print(f"wrote {texture_dir / 'face_SDF.png'}")
        print(f"wrote masks to {mask_dir}")
        return

    threshold = threshold_from_masks(masks)
    if args.invert_dessi:
        threshold = 1.0 - threshold
    soft_face_mask = soften_mask_edges(face_mask, 2.0)
    save_rgba(texture_dir / "face_SDF.png", threshold, soft_face_mask)
    save_rgba(texture_dir / "face_SDF_from_article_masks.png", threshold, soft_face_mask)
    save_preview(texture_dir / "face_SDF_preview.png", threshold, soft_face_mask)
    print(f"wrote {texture_dir / 'face_SDF.png'}")
    print(f"wrote masks to {mask_dir}")


def main():
    parser = argparse.ArgumentParser(description="Generate article-style face SDF threshold maps.")
    parser.add_argument("--example-dir", default=r"C:\Users\25775\Downloads\PlaneB\PlaneB\example")
    parser.add_argument("--reference", default=r"C:\Users\25775\Downloads\PlaneB\PlaneB\wow.png")
    parser.add_argument("--output-dir", default=r"Assets\Models\Nikke-Dessi\Texture\FaceSDFDebug")
    parser.add_argument("--texture-dir", default=r"Assets\Models\Nikke-Dessi\Texture")
    parser.add_argument("--size", type=int, default=2048)
    parser.add_argument("--planeb-exe", default=r"C:\Users\25775\Downloads\PlaneB\PlaneB\PlaneB.exe")
    parser.add_argument("--blendingtimes", type=int, default=50)
    parser.add_argument("--python-sdf", action="store_true")
    parser.add_argument("--skip-example", action="store_true")
    parser.add_argument("--skip-dessi", action="store_true")
    parser.add_argument("--invert-dessi", action="store_true")
    args = parser.parse_args()
    if args.python_sdf:
        args.planeb_exe = None

    if not args.skip_example:
        build_example(args)
    if not args.skip_dessi:
        build_dessi(args)


if __name__ == "__main__":
    main()
