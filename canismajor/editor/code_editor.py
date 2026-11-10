"""Editor de código no estilo do Sirius: margem de números, pares que se fecham e atalhos do dia a dia."""

from __future__ import annotations

import re

from PySide6.QtCore import QRect, QSize, QStringListModel, Qt, Signal
from PySide6.QtGui import (QColor, QFont, QFontMetricsF, QKeySequence, QPainter, QTextCharFormat, QTextCursor,
                           QTextFormat)
from PySide6.QtWidgets import (QCompleter, QHBoxLayout, QLineEdit, QPlainTextEdit, QPushButton, QTextEdit,
                               QVBoxLayout, QWidget)

from . import icons
from .highlighter import Highlighter, completion_words
from .theme import CODE_FONT, Palette

PAIRS = {"(": ")", "[": "]", "{": "}", '"': '"', "'": "'"}
CLOSERS = set(PAIRS.values())
COMMENT = {"python": "#", "glsl": "//"}


class _Gutter(QWidget):
    def __init__(self, editor: "CodeEditor"):
        super().__init__(editor)
        self.editor = editor

    def sizeHint(self) -> QSize:
        return QSize(self.editor.gutter_width(), 0)

    def paintEvent(self, event):
        self.editor.paint_gutter(event)


class CodeEditor(QPlainTextEdit):
    cursor_moved = Signal(int, int)

    def __init__(self, language: str, palette: Palette, code_size: float = 13.5, auto_pairs: bool = True):
        super().__init__()
        self.language = language
        self.palette_ = palette
        self.auto_pairs = auto_pairs
        self.errors: dict[int, str] = {}
        self.setFrameStyle(0)
        self.setLineWrapMode(QPlainTextEdit.LineWrapMode.NoWrap)
        self.gutter = _Gutter(self)
        self.highlighter = Highlighter(self.document(), language, palette)
        self.set_code_size(code_size)
        self.blockCountChanged.connect(self._update_margins)
        self.updateRequest.connect(self._update_gutter)
        self.cursorPositionChanged.connect(self._on_cursor)
        self._update_margins()
        self.apply_palette(palette)

        self.completer = QCompleter(self)
        self.completer.setWidget(self)
        self.completer.setCaseSensitivity(Qt.CaseSensitivity.CaseInsensitive)
        self.completer.setCompletionMode(QCompleter.CompletionMode.PopupCompletion)
        self.completer.setModel(QStringListModel(completion_words(language), self.completer))
        self.completer.activated.connect(self._insert_completion)

    # ---------- aparência ----------

    def apply_palette(self, p: Palette) -> None:
        self.palette_ = p
        self.highlighter.set_palette(p)
        self._restyle()
        self.gutter.update()
        self._refresh_extra()

    def _restyle(self) -> None:
        # A regra global "*" do tema ganharia de setFont, então a fonte mono vai na folha de estilo.
        p = self.palette_
        sel = QColor(p.selection)
        pt = getattr(self, "code_px", 13.5) * 0.75
        self.setStyleSheet(
            f"QPlainTextEdit {{ background: transparent; border: none; color: {p.ink}; "
            f"font-family: \"{CODE_FONT}\"; font-size: {pt:.2f}pt; "
            f"selection-background-color: rgba({sel.red()},{sel.green()},{sel.blue()},{sel.alpha()}); "
            f"selection-color: {p.ink}; padding-left: 6px; }}"
        )
        self.gutter.setStyleSheet(f"font-family: \"{CODE_FONT}\"; font-size: {pt:.2f}pt;")

    def set_code_size(self, px: float) -> None:
        self.code_px = px
        font = QFont(CODE_FONT)
        font.setPointSizeF(px * 0.75)
        font.setStyleHint(QFont.StyleHint.Monospace)
        self.setFont(font)
        self.gutter.setFont(font)
        if hasattr(self, "palette_"):
            self._restyle()
        metrics = QFontMetricsF(font)
        self.setTabStopDistance(metrics.horizontalAdvance(" ") * 4)
        self._update_margins()

    def set_word_wrap(self, on: bool) -> None:
        self.setLineWrapMode(QPlainTextEdit.LineWrapMode.WidgetWidth if on else QPlainTextEdit.LineWrapMode.NoWrap)

    # ---------- margem ----------

    def gutter_width(self) -> int:
        digits = max(2, len(str(self.blockCount())))
        return int(QFontMetricsF(self.font()).horizontalAdvance("9") * digits) + 30

    def _update_margins(self, *_):
        self.setViewportMargins(self.gutter_width(), 8, 0, 0)

    def _update_gutter(self, rect, dy):
        if dy:
            self.gutter.scroll(0, dy)
        else:
            self.gutter.update(0, rect.y(), self.gutter.width(), rect.height())

    def resizeEvent(self, event):
        super().resizeEvent(event)
        cr = self.contentsRect()
        self.gutter.setGeometry(QRect(cr.left(), cr.top() + 8, self.gutter_width(), cr.height() - 8))

    def paint_gutter(self, event):
        p = self.palette_
        painter = QPainter(self.gutter)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        block = self.firstVisibleBlock()
        number = block.blockNumber()
        top = self.blockBoundingGeometry(block).translated(self.contentOffset()).top()
        bottom = top + self.blockBoundingRect(block).height()
        current = self.textCursor().blockNumber()
        width = self.gutter.width()
        line_h = self.fontMetrics().height()
        while block.isValid() and top <= event.rect().bottom():
            if block.isVisible() and bottom >= event.rect().top():
                line = number + 1
                font = self.font()
                font.setWeight(QFont.Weight.DemiBold if number == current else QFont.Weight.Normal)
                painter.setFont(font)
                painter.setPen(QColor(p.ink if number == current else p.faint))
                painter.drawText(0, int(top), width - 18, line_h, Qt.AlignmentFlag.AlignRight, str(line))
                if line in self.errors:
                    painter.setPen(Qt.PenStyle.NoPen)
                    painter.setBrush(QColor(p.error))
                    painter.drawEllipse(width - 12, int(top + line_h / 2 - 3), 6, 6)
            block = block.next()
            top = bottom
            bottom = top + self.blockBoundingRect(block).height()
            number += 1

    # ---------- erros ----------

    def set_errors(self, errors: dict[int, str]) -> None:
        self.errors = dict(errors)
        self._refresh_extra()
        self.gutter.update()

    def _refresh_extra(self):
        selections = []
        for line in self.errors:
            block = self.document().findBlockByNumber(line - 1)
            if not block.isValid():
                continue
            sel = QTextEdit.ExtraSelection()
            fmt = QTextCharFormat()
            fmt.setUnderlineStyle(QTextCharFormat.UnderlineStyle.WaveUnderline)
            fmt.setUnderlineColor(QColor(self.palette_.error))
            sel.format = fmt
            cursor = QTextCursor(block)
            cursor.movePosition(QTextCursor.MoveOperation.StartOfBlock)
            text = block.text()
            cursor.movePosition(QTextCursor.MoveOperation.Right, n=len(text) - len(text.lstrip()))
            cursor.movePosition(QTextCursor.MoveOperation.EndOfBlock, QTextCursor.MoveMode.KeepAnchor)
            sel.cursor = cursor
            selections.append(sel)
        self.setExtraSelections(selections)

    def event(self, ev):
        if ev.type() == ev.Type.ToolTip and self.errors:
            cursor = self.cursorForPosition(ev.pos() - self.viewport().pos())
            line = cursor.blockNumber() + 1
            if line in self.errors:
                from PySide6.QtWidgets import QToolTip
                QToolTip.showText(ev.globalPos(), self.errors[line], self)
                return True
        return super().event(ev)

    # ---------- navegação ----------

    def _on_cursor(self):
        c = self.textCursor()
        self.cursor_moved.emit(c.blockNumber() + 1, c.positionInBlock() + 1)
        self.gutter.update()

    def go_to_line(self, line: int, column: int = 1) -> None:
        block = self.document().findBlockByNumber(max(0, line - 1))
        if not block.isValid():
            block = self.document().lastBlock()
        cursor = QTextCursor(block)
        cursor.movePosition(QTextCursor.MoveOperation.Right, n=min(max(0, column - 1), len(block.text())))
        self.setTextCursor(cursor)
        self.centerCursor()
        self.setFocus()

    def find_text(self, text: str, backward: bool = False) -> bool:
        from PySide6.QtGui import QTextDocument
        flags = QTextDocument.FindFlag.FindBackward if backward else QTextDocument.FindFlag(0)
        if self.find(text, flags):
            return True
        cursor = self.textCursor()
        cursor.movePosition(QTextCursor.MoveOperation.End if backward else QTextCursor.MoveOperation.Start)
        self.setTextCursor(cursor)
        return self.find(text, flags)

    # ---------- edição ----------

    def _selected_blocks(self):
        c = self.textCursor()
        doc = self.document()
        start = doc.findBlock(c.selectionStart())
        end = doc.findBlock(c.selectionEnd())
        if c.hasSelection() and c.selectionEnd() == end.position() and end != start:
            end = end.previous()
        return start, end

    def toggle_comment(self) -> None:
        mark = COMMENT.get(self.language)
        if not mark:
            return
        start, end = self._selected_blocks()
        blocks = []
        b = start
        while b.isValid():
            blocks.append(b)
            if b == end:
                break
            b = b.next()
        lines = [b.text() for b in blocks if b.text().strip()]
        all_commented = bool(lines) and all(l.lstrip().startswith(mark) for l in lines)
        indent = min((len(l) - len(l.lstrip()) for l in lines), default=0)
        cursor = self.textCursor()
        cursor.beginEditBlock()
        for b in blocks:
            text = b.text()
            if not text.strip():
                continue
            c = QTextCursor(b)
            if all_commented:
                i = text.find(mark)
                n = len(mark) + (1 if text[i + len(mark):i + len(mark) + 1] == " " else 0)
                c.setPosition(b.position() + i)
                c.setPosition(b.position() + i + n, QTextCursor.MoveMode.KeepAnchor)
                c.removeSelectedText()
            else:
                c.setPosition(b.position() + indent)
                c.insertText(mark + " ")
        cursor.endEditBlock()

    def move_lines(self, direction: int) -> None:
        start, end = self._selected_blocks()
        doc = self.document()
        if direction < 0 and not start.previous().isValid():
            return
        if direction > 0 and not end.next().isValid():
            return
        cursor = self.textCursor()
        anchor_off = cursor.anchor() - start.position()
        pos_off = cursor.position() - start.position()
        cursor.beginEditBlock()
        sel = QTextCursor(doc)
        sel.setPosition(start.position())
        sel.setPosition(end.position() + end.length() - 1, QTextCursor.MoveMode.KeepAnchor)
        text = sel.selectedText().replace(" ", "\n")
        if direction < 0:
            other = start.previous()
            sel.setPosition(other.position())
            sel.setPosition(end.position() + end.length() - 1, QTextCursor.MoveMode.KeepAnchor)
            sel.insertText(text + "\n" + other.text())
            new_start = other.position()
        else:
            other = end.next()
            sel.setPosition(start.position())
            sel.setPosition(other.position() + other.length() - 1, QTextCursor.MoveMode.KeepAnchor)
            sel.insertText(other.text() + "\n" + text)
            new_start = start.position() + len(other.text()) + 1
        cursor.endEditBlock()
        c = self.textCursor()
        c.setPosition(new_start + anchor_off)
        c.setPosition(new_start + pos_off, QTextCursor.MoveMode.KeepAnchor)
        self.setTextCursor(c)

    def duplicate_lines(self, down: bool = True) -> None:
        start, end = self._selected_blocks()
        sel = QTextCursor(self.document())
        sel.setPosition(start.position())
        sel.setPosition(end.position() + end.length() - 1, QTextCursor.MoveMode.KeepAnchor)
        text = sel.selectedText().replace(" ", "\n")
        cursor = self.textCursor()
        cursor.beginEditBlock()
        sel.clearSelection()
        sel.setPosition(end.position() + end.length() - 1)
        sel.insertText("\n" + text)
        cursor.endEditBlock()
        if down:
            offset = len(text) + 1
            c = self.textCursor()
            a, p = c.anchor() + offset, c.position() + offset
            c.setPosition(a)
            c.setPosition(p, QTextCursor.MoveMode.KeepAnchor)
            self.setTextCursor(c)

    def _indent_selection(self, dedent: bool) -> None:
        start, end = self._selected_blocks()
        cursor = self.textCursor()
        cursor.beginEditBlock()
        b = start
        while b.isValid():
            c = QTextCursor(b)
            if dedent:
                text = b.text()
                n = min(4, len(text) - len(text.lstrip(" ")))
                if n:
                    c.movePosition(QTextCursor.MoveOperation.Right, QTextCursor.MoveMode.KeepAnchor, n)
                    c.removeSelectedText()
            else:
                c.insertText("    ")
            if b == end:
                break
            b = b.next()
        cursor.endEditBlock()

    def keyPressEvent(self, event):
        popup = self.completer.popup()
        if popup.isVisible():
            if event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter, Qt.Key.Key_Tab):
                index = popup.currentIndex()
                if index.isValid():
                    self._insert_completion(index.data())
                popup.hide()
                return
            if event.key() == Qt.Key.Key_Escape:
                popup.hide()
                return

        key, mods, text = event.key(), event.modifiers(), event.text()
        ctrl = bool(mods & Qt.KeyboardModifier.ControlModifier)
        alt = bool(mods & Qt.KeyboardModifier.AltModifier)
        shift = bool(mods & Qt.KeyboardModifier.ShiftModifier)
        cursor = self.textCursor()

        if ctrl and alt:  # AltGr no ABNT2 chega como Ctrl+Alt: nunca é atalho
            super().keyPressEvent(event)
            return
        if ctrl and key in (Qt.Key.Key_Slash, Qt.Key.Key_Semicolon):
            self.toggle_comment()
            return
        if alt and not ctrl and key in (Qt.Key.Key_Up, Qt.Key.Key_Down):
            if shift:
                self.duplicate_lines(down=key == Qt.Key.Key_Down)
            else:
                self.move_lines(-1 if key == Qt.Key.Key_Up else 1)
            return
        if key == Qt.Key.Key_Tab and not ctrl:
            if cursor.hasSelection() and cursor.selectedText().find(" ") >= 0:
                self._indent_selection(False)
            else:
                cursor.insertText("    ")
            return
        if key == Qt.Key.Key_Backtab:
            self._indent_selection(True)
            return
        if key in (Qt.Key.Key_Return, Qt.Key.Key_Enter) and not ctrl:
            self._newline()
            return
        if key == Qt.Key.Key_Backspace and not cursor.hasSelection() and not ctrl:
            block_text = cursor.block().text()[: cursor.positionInBlock()]
            after = cursor.block().text()[cursor.positionInBlock():cursor.positionInBlock() + 1]
            if self.auto_pairs and block_text and block_text[-1] in PAIRS and PAIRS[block_text[-1]] == after:
                cursor.deleteChar()
                cursor.deletePreviousChar()
                return
            if block_text and not block_text.strip(" ") and len(block_text) >= 1:
                n = (len(block_text) - 1) % 4 + 1
                cursor.movePosition(QTextCursor.MoveOperation.Left, QTextCursor.MoveMode.KeepAnchor, n)
                cursor.removeSelectedText()
                return
        if self.auto_pairs and text and not ctrl:
            after = cursor.block().text()[cursor.positionInBlock():cursor.positionInBlock() + 1]
            if text in CLOSERS and after == text and not cursor.hasSelection():
                cursor.movePosition(QTextCursor.MoveOperation.Right)
                self.setTextCursor(cursor)
                return
            if text in PAIRS:
                close = PAIRS[text]
                if cursor.hasSelection():
                    selected = cursor.selectedText()
                    cursor.insertText(text + selected + close)
                    return
                before = cursor.block().text()[cursor.positionInBlock() - 1:cursor.positionInBlock()]
                quote = text in "\"'"
                if (not after or after in " )]}:,;") and not (quote and before.isalnum()):
                    cursor.insertText(text + close)
                    cursor.movePosition(QTextCursor.MoveOperation.Left)
                    self.setTextCursor(cursor)
                    return

        super().keyPressEvent(event)
        if text and (text.isalnum() or text == "_"):
            self._maybe_complete()
        elif popup.isVisible():
            popup.hide()

    def _newline(self):
        cursor = self.textCursor()
        line = cursor.block().text()
        before = line[: cursor.positionInBlock()]
        after = line[cursor.positionInBlock():]
        indent = re.match(r"[ \t]*", line).group()
        stripped = before.rstrip()
        extra = ""
        if self.language == "python" and stripped.endswith(":"):
            extra = "    "
        elif stripped.endswith(("{", "(", "[")):
            extra = "    "
        cursor.beginEditBlock()
        if extra and after[:1] in ("}", ")", "]"):
            cursor.insertText("\n" + indent + extra + "\n" + indent)
            cursor.movePosition(QTextCursor.MoveOperation.Up)
            cursor.movePosition(QTextCursor.MoveOperation.EndOfLine)
        else:
            cursor.insertText("\n" + indent + extra)
        cursor.endEditBlock()
        self.setTextCursor(cursor)

    # ---------- autocompletar ----------

    def _word_before(self) -> str:
        c = self.textCursor()
        text = c.block().text()[: c.positionInBlock()]
        m = re.search(r"[A-Za-z_]\w*$", text)
        return m.group() if m else ""

    def _maybe_complete(self):
        prefix = self._word_before()
        if len(prefix) < 2:
            self.completer.popup().hide()
            return
        words = set(completion_words(self.language))
        words |= set(re.findall(r"[A-Za-z_]\w{3,}", self.toPlainText()))
        words.discard(prefix)
        model: QStringListModel = self.completer.model()
        model.setStringList(sorted(words, key=str.lower))
        self.completer.setCompletionPrefix(prefix)
        if self.completer.completionCount() == 0:
            self.completer.popup().hide()
            return
        popup = self.completer.popup()
        popup.setCurrentIndex(self.completer.completionModel().index(0, 0))
        rect = self.cursorRect()
        rect.setWidth(min(320, popup.sizeHintForColumn(0) + popup.verticalScrollBar().sizeHint().width() + 24))
        self.completer.complete(rect)

    def _insert_completion(self, word: str):
        prefix = self._word_before()
        c = self.textCursor()
        c.movePosition(QTextCursor.MoveOperation.Left, QTextCursor.MoveMode.KeepAnchor, len(prefix))
        c.insertText(word)
        self.setTextCursor(c)


class CodePage(QWidget):
    """Editor com a barra de busca (Ctrl F) por cima."""

    def __init__(self, editor: CodeEditor, palette: Palette):
        super().__init__()
        self.setObjectName("Page")
        self.editor = editor
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        self.find_bar = QWidget()
        bar = QHBoxLayout(self.find_bar)
        bar.setContentsMargins(12, 6, 12, 6)
        self.find_input = QLineEdit()
        self.find_input.setPlaceholderText("Procurar no arquivo")
        self.find_input.returnPressed.connect(lambda: self.editor.find_text(self.find_input.text()))
        close = QPushButton()
        close.setProperty("role", "tool")
        close.setIcon(icons.icon("x", palette.muted))
        close.clicked.connect(self.hide_find)
        bar.addWidget(self.find_input, 1)
        bar.addWidget(close)
        self.find_bar.hide()
        layout.addWidget(self.find_bar)
        layout.addWidget(editor, 1)

    def show_find(self):
        selected = self.editor.textCursor().selectedText()
        if selected and " " not in selected:
            self.find_input.setText(selected)
        self.find_bar.show()
        self.find_input.setFocus()
        self.find_input.selectAll()

    def hide_find(self):
        self.find_bar.hide()
        self.editor.setFocus()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_Escape and self.find_bar.isVisible():
            self.hide_find()
            return
        super().keyPressEvent(event)
