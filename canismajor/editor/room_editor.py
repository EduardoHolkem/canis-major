"""Editor de salas: coloque, arraste e apague objetos sobre a grade."""

from __future__ import annotations

import copy
import json

from PySide6.QtCore import QPointF, QRectF, Qt, Signal
from PySide6.QtGui import QBrush, QColor, QIcon, QPainter, QPen, QTransform
from PySide6.QtWidgets import (QCheckBox, QColorDialog, QFormLayout, QGraphicsItem, QGraphicsPixmapItem,
                               QGraphicsRectItem, QGraphicsScene, QGraphicsView, QHBoxLayout, QLabel,
                               QListWidget, QListWidgetItem, QPushButton, QSpinBox, QVBoxLayout, QWidget)

from ..project import Project, load_room, save_room
from .resources import object_pixmap, thumbnail
from .theme import Palette


class InstanceItem(QGraphicsPixmapItem):
    def __init__(self, page: "RoomPage", data: dict, pixmap, origin):
        super().__init__(pixmap)
        self.page = page
        self.data_ = data
        self.origin = origin
        self.setOffset(-origin[0], -origin[1])
        self.setPos(data.get("x", 0), data.get("y", 0))
        self.setFlags(QGraphicsItem.GraphicsItemFlag.ItemIsMovable | QGraphicsItem.GraphicsItemFlag.ItemIsSelectable
                      | QGraphicsItem.GraphicsItemFlag.ItemSendsGeometryChanges)
        self.setTransformationMode(Qt.TransformationMode.FastTransformation)
        self.setToolTip(data["object"])
        self.setCursor(Qt.CursorShape.SizeAllCursor)

    def itemChange(self, change, value):
        if change == QGraphicsItem.GraphicsItemChange.ItemPositionChange:
            grid = self.page.grid_size()
            if grid > 1 and not (self.page.view.free_move):
                # O canto do sprite encaixa na grade, não o ponto de origem.
                ox, oy = self.origin
                value = QPointF(round((value.x() - ox) / grid) * grid + ox, round((value.y() - oy) / grid) * grid + oy)
            return value
        return super().itemChange(change, value)

    def paint(self, painter, option, widget=None):
        super().paint(painter, option, widget)
        if self.isSelected():
            pen = QPen(QColor(self.page.palette_.accent), 0)
            painter.setPen(pen)
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawRect(self.boundingRect())


class RoomView(QGraphicsView):
    def __init__(self, page: "RoomPage"):
        super().__init__()
        self.page = page
        self.free_move = False
        self._panning = None
        self._press_snapshot = None
        self.setRenderHint(QPainter.RenderHint.Antialiasing, False)
        self.setDragMode(QGraphicsView.DragMode.NoDrag)
        self.setTransformationAnchor(QGraphicsView.ViewportAnchor.AnchorUnderMouse)
        self.setViewportUpdateMode(QGraphicsView.ViewportUpdateMode.FullViewportUpdate)

    def drawForeground(self, painter: QPainter, rect: QRectF):
        page = self.page
        grid = page.grid_size()
        w, h = page.room["width"], page.room["height"]
        if grid > 1 and self.transform().m11() * grid >= 6:
            c = QColor(page.palette_.ink)
            c.setAlpha(22)
            painter.setPen(QPen(c, 0))
            x = 0
            while x <= w:
                painter.drawLine(QPointF(x, 0), QPointF(x, h))
                x += grid
            y = 0
            while y <= h:
                painter.drawLine(QPointF(0, y), QPointF(w, y))
                y += grid
        view_w, view_h = page.project.width, page.project.height
        c = QColor(page.palette_.accent)
        c.setAlpha(150)
        pen = QPen(c, 0, Qt.PenStyle.DashLine)
        painter.setPen(pen)
        painter.drawRect(QRectF(0, 0, min(view_w, w), min(view_h, h)))

    def wheelEvent(self, event):
        if event.modifiers() & Qt.KeyboardModifier.ControlModifier:
            factor = 1.25 if event.angleDelta().y() > 0 else 0.8
            new = self.transform().m11() * factor
            if 0.25 <= new <= 16:
                self.scale(factor, factor)
            return
        super().wheelEvent(event)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.MiddleButton:
            self._panning = event.position()
            self.setCursor(Qt.CursorShape.ClosedHandCursor)
            return
        pos = self.mapToScene(event.position().toPoint())
        item = self.itemAt(event.position().toPoint())
        inst = item if isinstance(item, InstanceItem) else None
        if event.button() == Qt.MouseButton.RightButton:
            if inst:
                self.page.push_undo()
                self.page.remove_item(inst)
            return
        if event.button() == Qt.MouseButton.LeftButton:
            self.free_move = bool(event.modifiers() & Qt.KeyboardModifier.AltModifier)
            if inst is None:
                obj = self.page.selected_object()
                if obj and 0 <= pos.x() < self.page.room["width"] and 0 <= pos.y() < self.page.room["height"]:
                    self.page.push_undo()
                    grid = self.page.grid_size()
                    x, y = pos.x(), pos.y()
                    if grid > 1 and not self.free_move:
                        x, y = (x // grid) * grid, (y // grid) * grid
                        _, origin = object_pixmap(self.page.project, obj, self.page.palette_.accent)
                        x, y = x + origin[0], y + origin[1]
                    self.page.add_instance({"object": obj, "x": int(x), "y": int(y)}, select=True)
                    return
            else:
                self._press_snapshot = self.page.snapshot()
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        if self._panning is not None:
            delta = event.position() - self._panning
            self._panning = event.position()
            self.horizontalScrollBar().setValue(self.horizontalScrollBar().value() - int(delta.x()))
            self.verticalScrollBar().setValue(self.verticalScrollBar().value() - int(delta.y()))
            return
        super().mouseMoveEvent(event)

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.MouseButton.MiddleButton:
            self._panning = None
            self.unsetCursor()
            return
        super().mouseReleaseEvent(event)
        if self._press_snapshot is not None:
            if self._press_snapshot != self.page.snapshot():
                self.page.undo_stack.append(self._press_snapshot)
                self.page.mark_modified()
            self._press_snapshot = None

    def keyPressEvent(self, event):
        if event.key() in (Qt.Key.Key_Delete, Qt.Key.Key_Backspace):
            items = [i for i in self.scene().selectedItems() if isinstance(i, InstanceItem)]
            if items:
                self.page.push_undo()
                for i in items:
                    self.page.remove_item(i)
            return
        super().keyPressEvent(event)


class RoomPage(QWidget):
    modified = Signal()

    def __init__(self, project: Project, name: str, palette: Palette):
        super().__init__()
        self.setObjectName("Page")
        self.project = project
        self.name = name
        self.path = project.path("room", name)
        self.palette_ = palette
        self.room = load_room(self.path, project)
        self.undo_stack: list[str] = []
        self.redo_stack: list[str] = []
        self.dirty = False

        root = QHBoxLayout(self)
        root.setContentsMargins(10, 6, 0, 10)
        root.setSpacing(10)

        side = QWidget()
        side.setFixedWidth(220)
        side_l = QVBoxLayout(side)
        side_l.setContentsMargins(0, 0, 0, 0)
        side_l.setSpacing(6)

        side_l.addWidget(self._section("Objetos"))
        self.object_list = QListWidget()
        self.object_list.setMinimumHeight(120)
        side_l.addWidget(self.object_list, 2)

        side_l.addWidget(self._section("Sala"))
        form = QFormLayout()
        form.setContentsMargins(0, 0, 0, 0)
        form.setHorizontalSpacing(10)
        form.setVerticalSpacing(6)
        self.width_box = self._spin(16, 100000, int(self.room["width"]))
        self.height_box = self._spin(16, 100000, int(self.room["height"]))
        self.grid_box = self._spin(1, 512, int(self.room.get("grid", 16)))
        self.bg_button = QPushButton()
        self.bg_button.setProperty("role", "secondary")
        self.bg_button.clicked.connect(self._pick_background)
        form.addRow(self._label("Largura"), self.width_box)
        form.addRow(self._label("Altura"), self.height_box)
        form.addRow(self._label("Grade"), self.grid_box)
        form.addRow(self._label("Fundo"), self.bg_button)
        side_l.addLayout(form)
        self.start_check = QCheckBox("Sala inicial do jogo")
        self.start_check.setChecked(project.start_room == name)
        self.start_check.toggled.connect(self._toggle_start)
        side_l.addWidget(self.start_check)

        side_l.addWidget(self._section("Shaders de tela"))
        self.post_list = QListWidget()
        self.post_list.setMinimumHeight(70)
        self.post_list.itemChanged.connect(self._post_changed)
        side_l.addWidget(self.post_list, 1)

        hint = QLabel("Clique para colocar o objeto escolhido. Arraste para mover (Alt solta da grade). "
                      "Botão direito ou Delete apaga. Ctrl + roda dá zoom, botão do meio arrasta a vista.")
        hint.setProperty("role", "muted")
        hint.setWordWrap(True)
        side_l.addWidget(hint)
        root.addWidget(side)

        self.scene = QGraphicsScene(self)
        self.view = RoomView(self)
        self.view.setScene(self.scene)
        root.addWidget(self.view, 1)

        self.background_item = QGraphicsRectItem()
        self.background_item.setPen(Qt.PenStyle.NoPen)
        self.background_item.setZValue(-1000)
        self.scene.addItem(self.background_item)

        for box in (self.width_box, self.height_box):
            box.valueChanged.connect(self._size_changed)
        self.grid_box.valueChanged.connect(self._grid_changed)

        self.refresh_lists()
        self._apply_room()
        self._fitted = False

    def showEvent(self, event):
        super().showEvent(event)
        if not self._fitted:
            from PySide6.QtCore import QTimer
            QTimer.singleShot(0, self.fit_view)

    def fit_view(self) -> None:
        """Enquadra a sala inteira, com zoom inteiro para o pixel art ficar nítido."""
        vw, vh = self.view.viewport().width() - 24, self.view.viewport().height() - 24
        w, h = self.room["width"], self.room["height"]
        if vw <= 0 or vh <= 0:
            return
        k = min(vw / w, vh / h)
        k = float(int(k)) if k >= 2 else k
        self.view.setTransform(QTransform.fromScale(k, k))
        self.view.centerOn(w / 2, h / 2)
        self._fitted = True

    # ---------- montagem ----------

    def _section(self, text):
        label = QLabel(text)
        label.setProperty("role", "section")
        return label

    def _label(self, text):
        label = QLabel(text)
        label.setProperty("role", "muted")
        return label

    def _spin(self, lo, hi, value):
        box = QSpinBox()
        box.setRange(lo, hi)
        box.setValue(value)
        return box

    def refresh_lists(self) -> None:
        current = self.selected_object()
        self.object_list.clear()
        for obj in self.project.resources("object"):
            pix, _ = object_pixmap(self.project, obj, self.palette_.accent)
            item = QListWidgetItem(QIcon(thumbnail(pix)), obj)
            self.object_list.addItem(item)
            if obj == current:
                item.setSelected(True)
                self.object_list.setCurrentItem(item)
        if self.object_list.currentItem() is None and self.object_list.count():
            self.object_list.setCurrentRow(0)

        self.post_list.blockSignals(True)
        self.post_list.clear()
        active = list(self.room.get("post_shaders", []))
        shaders = self.project.resources("shader")
        for name in active + [s for s in shaders if s not in active]:
            item = QListWidgetItem(name)
            item.setFlags(item.flags() | Qt.ItemFlag.ItemIsUserCheckable)
            item.setCheckState(Qt.CheckState.Checked if name in active else Qt.CheckState.Unchecked)
            if name not in shaders:
                item.setToolTip("Este shader não existe mais em shaders/.")
            self.post_list.addItem(item)
        self.post_list.blockSignals(False)

    def _apply_room(self) -> None:
        for item in [i for i in self.scene.items() if isinstance(i, InstanceItem)]:
            self.scene.removeItem(item)
        for inst in self.room.get("instances", []):
            self.add_instance(dict(inst), mark=False)
        self._update_background()

    def _update_background(self):
        w, h = self.room["width"], self.room["height"]
        bg = QColor(self.room.get("background", "#0D0F13"))
        self.background_item.setRect(QRectF(0, 0, w, h))
        self.background_item.setBrush(QBrush(bg))
        self.scene.setSceneRect(QRectF(-64, -64, w + 128, h + 128))
        self.bg_button.setText(bg.name().upper())
        text = "#16171A" if bg.lightness() > 140 else "#ECEEF2"
        self.bg_button.setStyleSheet(f"background: {bg.name()}; color: {text}; border-radius: 8px;")
        self.view.viewport().update()

    # ---------- instâncias ----------

    def selected_object(self) -> str | None:
        item = self.object_list.currentItem() if hasattr(self, "object_list") else None
        return item.text() if item else None

    def grid_size(self) -> int:
        return int(self.grid_box.value()) if hasattr(self, "grid_box") else 16

    def add_instance(self, data: dict, select=False, mark=True) -> InstanceItem:
        pix, origin = object_pixmap(self.project, data["object"], self.palette_.accent)
        item = InstanceItem(self, data, pix, origin)
        self.scene.addItem(item)
        if select:
            self.scene.clearSelection()
            item.setSelected(True)
        if mark:
            self.mark_modified()
        return item

    def remove_item(self, item: InstanceItem) -> None:
        self.scene.removeItem(item)
        self.mark_modified()

    def instances(self) -> list[dict]:
        items = [i for i in self.scene.items(Qt.SortOrder.AscendingOrder) if isinstance(i, InstanceItem)]
        out = []
        for item in items:
            data = dict(item.data_)
            data["x"] = round(item.pos().x(), 2) if item.pos().x() % 1 else int(item.pos().x())
            data["y"] = round(item.pos().y(), 2) if item.pos().y() % 1 else int(item.pos().y())
            out.append(data)
        return out

    # ---------- desfazer ----------

    def snapshot(self) -> str:
        room = copy.deepcopy(self.room)
        room["instances"] = self.instances()
        return json.dumps(room, sort_keys=True)

    def push_undo(self) -> None:
        self.undo_stack.append(self.snapshot())
        self.undo_stack = self.undo_stack[-100:]
        self.redo_stack.clear()

    def _restore(self, snap: str) -> None:
        self.room = json.loads(snap)
        for box, key in ((self.width_box, "width"), (self.height_box, "height"), (self.grid_box, "grid")):
            box.blockSignals(True)
            box.setValue(int(self.room.get(key, 16)))
            box.blockSignals(False)
        self._apply_room()
        self.refresh_lists()
        self.mark_modified()

    def undo(self) -> None:
        if self.undo_stack:
            self.redo_stack.append(self.snapshot())
            self._restore(self.undo_stack.pop())

    def redo(self) -> None:
        if self.redo_stack:
            self.undo_stack.append(self.snapshot())
            self._restore(self.redo_stack.pop())

    # ---------- propriedades ----------

    def _size_changed(self):
        self.push_undo()
        self.room["width"] = self.width_box.value()
        self.room["height"] = self.height_box.value()
        self._update_background()
        self.mark_modified()

    def _grid_changed(self):
        self.room["grid"] = self.grid_box.value()
        self.view.viewport().update()
        self.mark_modified()

    def _pick_background(self):
        current = QColor(self.room.get("background", "#0D0F13"))
        chosen = QColorDialog.getColor(current, self, "Cor de fundo da sala")
        if chosen.isValid():
            self.push_undo()
            self.room["background"] = chosen.name().upper()
            self._update_background()
            self.mark_modified()

    def _post_changed(self, _item=None):
        active = [self.post_list.item(i).text() for i in range(self.post_list.count())
                  if self.post_list.item(i).checkState() == Qt.CheckState.Checked]
        self.room["post_shaders"] = active
        self.mark_modified()

    def _toggle_start(self, on: bool):
        if on and self.project.start_room != self.name:
            self.project.start_room = self.name
            self.project.save()

    # ---------- arquivo ----------

    def mark_modified(self) -> None:
        if not self.dirty:
            self.dirty = True
        self.modified.emit()

    def save(self) -> None:
        self.room["instances"] = self.instances()
        save_room(self.path, self.room)
        self.dirty = False
        self.modified.emit()

    def apply_palette(self, palette: Palette) -> None:
        self.palette_ = palette
        self.view.viewport().update()
