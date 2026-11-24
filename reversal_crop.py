#!/usr/bin/env python3

from pathlib import Path
import sys

import gi
gi.require_version("Gtk", "4.0")
from gi.repository import Gtk, Gio, Gdk, GLib

from PIL import Image, ImageDraw, ImageChops, ImageOps


APP_DIR = Path.home() / "Dev/Reversal-Crop"
PREVIEW_PATH = APP_DIR / "preview.png"
OUTPUT_DIR = Path.home() / "Downloads" / "Reversal-Crop"

# Perfil geométrico recorrente do tema Reversal.
PROFILE_X = 4.5 / 64
PROFILE_SIZE = 55 / 64
PROFILE_RADIUS = 15 / 64


def render_icon(source, target, zoom=1.0, offset_x=0, offset_y=0, supersample=2):
    source = source.convert("RGBA")
    width, height = source.size

    crop_size = min(width, height) / zoom

    center_x = width / 2 + (offset_x / 100) * (width - crop_size) / 2
    center_y = height / 2 + (offset_y / 100) * (height - crop_size) / 2

    left = center_x - crop_size / 2
    top = center_y - crop_size / 2

    scale = supersample
    canvas_size = target * scale
    inner_size = round(target * PROFILE_SIZE * scale)
    position = round(target * PROFILE_X * scale)
    radius = round(target * PROFILE_RADIUS * scale)

    cropped = source.transform(
        (inner_size, inner_size),
        Image.Transform.EXTENT,
        (left, top, left + crop_size, top + crop_size),
        resample=Image.Resampling.BICUBIC
    )

    mask = Image.new("L", (inner_size, inner_size), 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle(
        (0, 0, inner_size - 1, inner_size - 1),
        radius=radius,
        fill=255
    )

    alpha = ImageChops.multiply(cropped.getchannel("A"), mask)
    cropped.putalpha(alpha)

    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    canvas.alpha_composite(cropped, (position, position))

    # Redução em alfa pré-multiplicado para evitar halos nas bordas.
    result = canvas.convert("RGBa").resize(
        (target, target),
        Image.Resampling.LANCZOS
    ).convert("RGBA")

    return result


def make_preview(icon):
    size = icon.width
    tile = 32

    background = Image.new("RGBA", (size, size), (220, 220, 220, 255))
    draw = ImageDraw.Draw(background)

    for y in range(0, size, tile):
        for x in range(0, size, tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle(
                    (x, y, x + tile - 1, y + tile - 1),
                    fill=(175, 175, 175, 255)
                )

    background.alpha_composite(icon)
    return background


class ReversalCrop(Gtk.Application):
    def __init__(self):
        super().__init__(application_id="br.local.reversalcrop")
        self.source = None
        self.source_path = None

    def do_activate(self):
        self.window = Gtk.ApplicationWindow(application=self)
        self.window.set_title("Reversal Crop")
        self.window.set_default_size(1000, 720)
        self.window.set_resizable(True)

        root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
        root.set_margin_top(18)
        root.set_margin_bottom(18)
        root.set_margin_start(18)
        root.set_margin_end(18)

        header = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
        title = Gtk.Label(label="Reversal Crop")
        title.add_css_class("title-1")
        header.append(title)

        open_button = Gtk.Button(label="Abrir imagem")
        open_button.add_css_class("suggested-action")
        open_button.connect("clicked", self.open_image)
        header.append(open_button)
        root.append(header)

        separator = Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL)
        root.append(separator)

        content = Gtk.Paned(orientation=Gtk.Orientation.HORIZONTAL)
        content.set_position(620)
        content.set_vexpand(True)

        preview_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        preview_box.set_hexpand(True)
        preview_box.set_vexpand(True)

        preview_title = Gtk.Label(label="Pré-visualização do ícone")
        preview_title.add_css_class("heading")
        preview_box.append(preview_title)

        self.picture = Gtk.Picture()
        self.picture.set_can_shrink(True)
        self.picture.set_size_request(320, 320)
        self.picture.set_content_fit(Gtk.ContentFit.CONTAIN)
        self.picture.set_hexpand(True)
        self.picture.set_vexpand(True)
        preview_box.append(self.picture)

        self.file_label = Gtk.Label(label="Abra uma imagem para começar")
        self.file_label.set_wrap(True)
        self.file_label.add_css_class("dim-label")
        preview_box.append(self.file_label)

        content.set_start_child(preview_box)

        controls = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
        controls.set_margin_start(18)
        controls.set_size_request(290, -1)

        controls_title = Gtk.Label(label="Enquadramento")
        controls_title.add_css_class("heading")
        controls.append(controls_title)

        self.zoom = self.add_scale(controls, "Zoom", 1.0, 4.0, 0.01, 1.0)
        self.offset_x = self.add_scale(controls, "Horizontal", -100, 100, 1, 0)
        self.offset_y = self.add_scale(controls, "Vertical", -100, 100, 1, 0)

        reset = Gtk.Button(label="Redefinir enquadramento")
        reset.connect("clicked", self.reset_controls)
        controls.append(reset)

        controls.append(Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL))

        profile = Gtk.Label(label="Perfil Reversal")
        profile.add_css_class("heading")
        controls.append(profile)

        info = Gtk.Label(
            label="Canvas quadrado\nÁrea útil: 55/64\nMargem: 4,5/64\nRaio: 15/64\nFundo externo transparente"
        )
        info.set_xalign(0)
        info.set_wrap(True)
        controls.append(info)

        controls.append(Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL))

        export_title = Gtk.Label(label="Exportação")
        export_title.add_css_class("heading")
        controls.append(export_title)

        export_button = Gtk.Button(label="Exportar PNG 512 + 2508")
        export_button.add_css_class("suggested-action")
        export_button.connect("clicked", self.export_all)
        controls.append(export_button)

        self.status = Gtk.Label(label="Aguardando imagem")
        self.status.set_wrap(True)
        self.status.set_xalign(0)
        controls.append(self.status)

        content.set_end_child(controls)
        root.append(content)

        self.window.set_child(root)
        self.window.present()

    def add_scale(self, parent, label, minimum, maximum, step, initial):
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)

        caption = Gtk.Label(label=label)
        caption.set_xalign(0)
        box.append(caption)

        scale = Gtk.Scale.new_with_range(
            Gtk.Orientation.HORIZONTAL,
            minimum,
            maximum,
            step
        )
        scale.set_draw_value(True)
        scale.set_value(initial)
        scale.connect("value-changed", self.update_preview)

        box.append(scale)
        parent.append(box)
        return scale

    def open_image(self, button):
        dialog = Gtk.FileChooserNative.new(
            "Selecionar imagem",
            self.window,
            Gtk.FileChooserAction.OPEN,
            "Abrir",
            "Cancelar"
        )

        file_filter = Gtk.FileFilter()
        file_filter.set_name("Imagens")
        for pattern in ("*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"):
            file_filter.add_pattern(pattern)
        dialog.add_filter(file_filter)

        dialog.connect("response", self.on_open_response)
        dialog.show()

    def on_open_response(self, dialog, response):
        try:
            if response != Gtk.ResponseType.ACCEPT:
                return

            file = dialog.get_file()
            if file is None or file.get_path() is None:
                self.status.set_text("Não foi possível obter o caminho da imagem.")
                return

            path = Path(file.get_path())

            with Image.open(path) as image:
                self.source = ImageOps.exif_transpose(image).convert("RGBA")

            self.source_path = path

            self.zoom.set_value(1)
            self.offset_x.set_value(0)
            self.offset_y.set_value(0)

            self.file_label.set_text(
                f"{path.name}\\n{self.source.width} × {self.source.height}"
            )

            print(
                f"Imagem carregada: {path} | "
                f"{self.source.width}x{self.source.height}",
                flush=True
            )

            self.update_preview()

        except Exception as error:
            import traceback
            traceback.print_exc()
            self.status.set_text(f"Erro ao carregar imagem: {error}")

        finally:
            dialog.destroy()

    def reset_controls(self, button):
        self.zoom.set_value(1)
        self.offset_x.set_value(0)
        self.offset_y.set_value(0)
        self.update_preview()

    def update_preview(self, *args):
        if self.source is None:
            return

        try:
            import io

            icon = render_icon(
                self.source,
                512,
                self.zoom.get_value(),
                self.offset_x.get_value(),
                self.offset_y.get_value(),
                supersample=2
            )

            preview = make_preview(icon)
            buffer = io.BytesIO()
            preview.save(buffer, format="PNG")

            texture = Gdk.Texture.new_from_bytes(
                GLib.Bytes.new(buffer.getvalue())
            )

            self.picture.set_paintable(texture)

            print(
                f"Prévia atualizada: {preview.width}x{preview.height} "
                f"| Widget: {self.picture.get_width()}x{self.picture.get_height()}",
                flush=True
            )

            self.status.set_text(
                f"Prévia atualizada | Zoom: {self.zoom.get_value():.2f}× | "
                f"Horizontal: {self.offset_x.get_value():.0f} | "
                f"Vertical: {self.offset_y.get_value():.0f}"
            )

        except Exception as error:
            import traceback
            traceback.print_exc()
            self.status.set_text(f"Erro na prévia: {error}")

    def export_all(self, button):
        if self.source is None:
            self.status.set_text("Abra uma imagem primeiro.")
            return

        try:
            OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
            base = self.source_path.stem

            settings = (
                self.zoom.get_value(),
                self.offset_x.get_value(),
                self.offset_y.get_value()
            )

            for target, supersample in ((512, 4), (2508, 2)):
                icon = render_icon(
                    self.source,
                    target,
                    *settings,
                    supersample=supersample
                )

                destination = OUTPUT_DIR / f"{base}-Reversal-{target}.png"
                icon.save(destination, "PNG", optimize=True)

            self.status.set_text(
                "Exportação concluída!\n"
                f"Pasta: {OUTPUT_DIR}\n"
                f"{base}-Reversal-512.png\n"
                f"{base}-Reversal-2508.png"
            )
        except Exception as error:
            self.status.set_text(f"Erro na exportação: {error}")


if __name__ == "__main__":
    app = ReversalCrop()
    sys.exit(app.run(None))
