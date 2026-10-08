"""Image helpers for review sheets, run inside Blender: load, resize, label and lay out PNGs.

Arrays are float32 RGBA, top row first (Blender's own pixel order is bottom row first; the
conversion happens only in `load` and `save`). Labels are rendered by Blender itself, as a
text object under an orthographic camera, so no imaging library is needed.
"""
import os
import tempfile

import bpy
import numpy as np


def load(path):
    image = bpy.data.images.load(path, check_existing=False)
    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape(height, width, 4)[::-1].copy()


def save(array, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    height, width = array.shape[:2]
    image = bpy.data.images.new("sheet", width, height, alpha=False)
    image.pixels.foreach_set(np.ascontiguousarray(array[::-1]).ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    return path


def resize(array, width, height):
    """Bilinear resample to width x height."""
    source_h, source_w = array.shape[:2]
    ys = np.clip((np.arange(height) + 0.5) * source_h / height - 0.5, 0, source_h - 1)
    xs = np.clip((np.arange(width) + 0.5) * source_w / width - 0.5, 0, source_w - 1)
    y0, x0 = np.floor(ys).astype(int), np.floor(xs).astype(int)
    y1, x1 = np.minimum(y0 + 1, source_h - 1), np.minimum(x0 + 1, source_w - 1)
    fy, fx = (ys - y0)[:, None, None], (xs - x0)[None, :, None]
    top = array[y0][:, x0] * (1 - fx) + array[y0][:, x1] * fx
    bottom = array[y1][:, x0] * (1 - fx) + array[y1][:, x1] * fx
    return (top * (1 - fy) + bottom * fy).astype(np.float32)


def nearest(array, factor):
    """Integer upscale without filtering, to inspect individual pixels."""
    return np.repeat(np.repeat(array, factor, axis=0), factor, axis=1)


def fit(array, width, height, background=0.12):
    """Scale to fit inside width x height, preserving aspect, centred on a flat background."""
    source_h, source_w = array.shape[:2]
    scale = min(width / source_w, height / source_h)
    w, h = max(1, round(source_w * scale)), max(1, round(source_h * scale))
    canvas = np.full((height, width, 4), background, dtype=np.float32)
    canvas[..., 3] = 1
    y, x = (height - h) // 2, (width - w) // 2
    canvas[y:y + h, x:x + w] = resize(array, w, h)
    return canvas


def label(text, width, height, size=0.6, colour=(0.92, 0.92, 0.92), background=(0.06, 0.06, 0.06)):
    """Render `text` centred in a width x height strip."""
    scene = bpy.data.scenes.new("label")
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "Standard"
    shading = scene.display.shading
    shading.light = "FLAT"
    shading.color_type = "SINGLE"
    shading.single_color = colour
    shading.background_type = "VIEWPORT"
    shading.background_color = [c ** 2.2 for c in background]
    curve = bpy.data.curves.new("label", "FONT")
    curve.body = text
    curve.align_x, curve.align_y = "CENTER", "CENTER"
    curve.size = size
    text_object = bpy.data.objects.new("label", curve)
    scene.collection.objects.link(text_object)
    camera = bpy.data.objects.new("label_camera", bpy.data.cameras.new("label_camera"))
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = max(1.0, width / height)      # world height 1 maps to the strip's height
    camera.location = (0, 0, 5)
    scene.collection.objects.link(camera)
    scene.camera = camera
    path = os.path.join(tempfile.gettempdir(), f"dk2_label_{os.getpid()}.png")
    scene.render.filepath = path
    scene.render.image_settings.file_format = "PNG"
    with bpy.context.temp_override(scene=scene):
        bpy.ops.render.render(write_still=True, scene=scene.name)
    bpy.data.objects.remove(text_object)
    bpy.data.curves.remove(curve)
    bpy.data.objects.remove(camera)
    bpy.data.scenes.remove(scene)
    strip = load(path)
    os.remove(path)
    return strip


def grid(cells, columns, labels=None, title=None, gap=8, label_height=44, background=0.12):
    """Lay out equally sized cells in rows, each with an optional label strip below it."""
    height, width = cells[0].shape[:2]
    rows = -(-len(cells) // columns)
    cell_h = height + (label_height if labels else 0)
    title_h = label_height + 12 if title else 0
    sheet = np.full((title_h + rows * (cell_h + gap) + gap, columns * (width + gap) + gap, 4),
                    background, dtype=np.float32)
    sheet[..., 3] = 1
    if title:
        sheet[:title_h] = label(title, sheet.shape[1], title_h, size=0.5)
    for index, cell in enumerate(cells):
        row, column = divmod(index, columns)
        y = title_h + gap + row * (cell_h + gap)
        x = gap + column * (width + gap)
        sheet[y:y + height, x:x + width] = cell
        if labels:
            sheet[y + height:y + cell_h, x:x + width] = label(labels[index], width, label_height, size=0.55)
    return sheet
