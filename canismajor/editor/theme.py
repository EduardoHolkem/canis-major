"""Temas claro e grafite, com as mesmas cores da IDE Sirius."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from PySide6.QtGui import QColor, QFontDatabase

FONT_DIR = Path(__file__).resolve().parent.parent / "assets" / "fonts"
UI_FONT = "Geist"
CODE_FONT = "Geist Mono"
ITALIC_FONT = "IBM Plex Mono"


@dataclass(frozen=True)
class Palette:
    name: str
    dark: bool
    ground: str
    sheet: str
    pop: str
    ink: str
    muted: str
    faint: str
    tint: str
    keyword: str
    type: str
    function: str
    string: str
    number: str
    accent: str
    selection: str
    veil: str
    capsule_bg: str
    capsule_ink: str
    capsule_muted: str
    capsule_mark: str
    on_accent: str
    run_bg: str
    run_ink: str
    error: str
    added: str
    checker_light: str
    checker_dark: str


# Valores copiados de Sirius/Theming/Palette.cs (#AARRGGBB quando há transparência).
CLARO = Palette(
    "claro", False,
    "#E8E9EC", "#FFFFFF", "#FFFFFF", "#16171A", "#5C5F67", "#6F727A",
    "#1216171A", "#8B2FC9", "#0B7A75", "#1F5FAF", "#2F7A3B", "#B45309",
    "#3F5F8F", "#333F5F8F", "#4716171A",
    "#16171A", "#F5F5F3", "#B4B7BD", "#24FFFFFF",
    "#FFFFFF", "#C9D6EA", "#16171A", "#C62828", "#2F7A3B", "#FFFFFF", "#DFE1E5",
)

GRAFITE = Palette(
    "grafite", True,
    "#0D0F13", "#16181D", "#21242B", "#ECEEF2", "#A3A8B3", "#8A8F9A",
    "#14FFFFFF", "#C792EA", "#5FD1C4", "#82AAFF", "#9FD5A0", "#F5A97F",
    "#9DB8E0", "#409DB8E0", "#80000000",
    "#E0C8CCD4", "#16181D", "#3F4249", "#1A16181D",
    "#0D0F13", "#3D5A85", "#FFFFFF", "#FF6B6B", "#9FD5A0", "#3A3D44", "#2C2F35",
)

PALETTES = {CLARO.name: CLARO, GRAFITE.name: GRAFITE}


def color(value: str) -> QColor:
    """Converte '#RRGGBB' ou '#AARRGGBB' em QColor."""
    return QColor(value)


def css(value: str) -> str:
    """O QSS entende rgba() com certeza; #AARRGGBB nem sempre."""
    c = QColor(value)
    return f"rgba({c.red()}, {c.green()}, {c.blue()}, {c.alpha()})"


def load_fonts() -> None:
    for file in sorted(FONT_DIR.glob("*.ttf")):
        QFontDatabase.addApplicationFont(str(file))


def stylesheet(p: Palette, ui_size: int = 13) -> str:
    c = {k: css(v) for k, v in p.__dict__.items() if isinstance(v, str) and v.startswith("#")}
    return f"""
* {{
    font-family: "{UI_FONT}";
    font-size: {ui_size}px;
    color: {c['ink']};
    outline: none;
}}
QWidget#Root, QDialog {{ background: {c['ground']}; }}
QWidget#Sheet {{ background: {c['sheet']}; border-radius: 12px; }}
QWidget#SheetInner, QStackedWidget#Pages, QWidget#Page {{ background: transparent; }}
QWidget#Pop, QFrame#Pop {{ background: {c['pop']}; border: 1px solid {c['tint']}; border-radius: 12px; }}
QLabel {{ background: transparent; }}
QLabel[role="muted"] {{ color: {c['muted']}; }}
QLabel[role="title"] {{ font-size: {ui_size + 9}px; font-weight: 600; }}
QLabel[role="section"] {{ color: {c['muted']}; font-size: {ui_size - 1}px; font-weight: 500; }}
QLabel[role="brand"] {{ font-weight: 700; }}
QLabel[role="error"] {{ color: {c['error']}; }}
QToolTip {{ background: {c['pop']}; color: {c['ink']}; border: 1px solid {c['tint']}; padding: 4px 8px; border-radius: 6px; }}

QPushButton, QToolButton {{
    background: transparent; border: none; border-radius: 7px; padding: 0 8px; min-height: 24px;
}}
QPushButton:hover, QToolButton:hover {{ background: {c['tint']}; }}
QPushButton:pressed, QToolButton:pressed {{ background: {c['selection']}; }}
QPushButton:disabled {{ color: {c['faint']}; }}
QPushButton[role="primary"] {{
    background: {c['accent']}; color: {c['on_accent']}; font-weight: 600; border-radius: 15px; min-height: 30px; padding: 0 16px;
}}
QPushButton[role="primary"]:hover {{ background: {c['accent']}; }}
QPushButton[role="secondary"] {{ background: {c['tint']}; border-radius: 15px; min-height: 30px; padding: 0 16px; }}
QPushButton[role="chrome"] {{ border-radius: 0; min-width: 40px; max-width: 40px; min-height: 32px; color: {c['muted']}; }}
QPushButton[role="close"] {{ border-radius: 0; min-width: 40px; max-width: 40px; min-height: 32px; }}
QPushButton[role="close"]:hover {{ background: #C42B1C; }}
QPushButton[role="pill"] {{
    background: {c['tint']}; border-radius: 14px; min-height: 28px; max-height: 28px; text-align: left; padding: 0 6px 0 12px;
    color: {c['muted']}; font-size: {ui_size - 1}px;
}}
QPushButton[role="segment"] {{ color: {c['muted']}; min-height: 24px; padding: 0 9px; font-size: {ui_size - 1}px; }}
QPushButton[role="segment"][on="true"] {{ background: {c['tint']}; color: {c['ink']}; font-weight: 600; }}
QPushButton[role="section"] {{ text-align: left; min-height: 30px; padding: 0 10px; }}
QPushButton[role="section"][on="true"] {{ background: {c['tint']}; font-weight: 600; }}
QPushButton[role="tool"] {{ min-width: 24px; max-width: 24px; min-height: 24px; max-height: 24px; padding: 0; border-radius: 6px; }}
QPushButton[role="tool"][on="true"] {{ background: {c['selection']}; }}
QPushButton[role="link"] {{ color: {c['accent']}; padding: 0 4px; text-align: left; }}

QWidget#Capsule {{ background: {c['capsule_bg']}; border-radius: 20px; }}
QWidget#Capsule QLabel, QWidget#Capsule QPushButton {{ color: {c['capsule_ink']}; }}
QWidget#Capsule QLabel[role="muted"] {{ color: {c['capsule_muted']}; }}
QWidget#Capsule QPushButton {{ border-radius: 16px; min-height: 32px; min-width: 32px; padding: 0 8px; }}
QWidget#Capsule QPushButton:hover {{ background: {c['capsule_mark']}; }}
QWidget#Capsule QPushButton[role="run"] {{
    background: {c['run_bg']}; color: {c['run_ink']}; font-weight: 600; padding: 0 14px 0 10px;
}}
QWidget#Capsule QPushButton[role="update"] {{ color: {c['capsule_ink']}; font-weight: 600; }}

QLineEdit, QSpinBox, QDoubleSpinBox, QComboBox, QPlainTextEdit#Field {{
    background: {c['tint']}; border: 1px solid transparent; border-radius: 8px; padding: 4px 8px;
    selection-background-color: {c['selection']}; selection-color: {c['ink']};
}}
QLineEdit:focus, QSpinBox:focus, QDoubleSpinBox:focus, QComboBox:focus {{ border: 1px solid {c['accent']}; }}
QLineEdit[role="bare"] {{ background: transparent; border: none; padding: 0; }}
QSpinBox::up-button, QSpinBox::down-button, QDoubleSpinBox::up-button, QDoubleSpinBox::down-button {{ width: 0; border: none; }}
QComboBox::drop-down {{ border: none; width: 18px; }}
QComboBox QAbstractItemView {{ background: {c['pop']}; border: 1px solid {c['tint']}; selection-background-color: {c['tint']}; padding: 4px; }}
QCheckBox {{ spacing: 8px; background: transparent; }}
QCheckBox::indicator {{ width: 14px; height: 14px; border-radius: 4px; border: 1px solid {c['muted']}; }}
QCheckBox::indicator:checked {{ background: {c['accent']}; border-color: {c['accent']}; }}

QTreeWidget, QListWidget {{ background: transparent; border: none; }}
QTreeWidget::item, QListWidget::item {{ min-height: 26px; border-radius: 6px; padding-left: 2px; }}
QTreeWidget::item:hover, QListWidget::item:hover {{ background: {c['tint']}; }}
QTreeWidget::item:selected, QListWidget::item:selected {{ background: {c['tint']}; color: {c['accent']}; font-weight: 600; }}
QTreeWidget::branch {{ background: transparent; }}
QListWidget::indicator {{ width: 13px; height: 13px; border-radius: 4px; border: 1px solid {c['muted']}; background: transparent; }}
QListWidget::indicator:checked {{ background: {c['accent']}; border-color: {c['accent']}; }}

QTabBar {{ background: transparent; }}
QTabBar::tab {{
    background: transparent; color: {c['muted']}; padding: 0 4px 0 10px; height: 34px; border: none; margin-right: 2px;
}}
QTabBar::tab:selected {{ color: {c['ink']}; font-weight: 600; }}
QTabBar::tab:hover {{ color: {c['ink']}; }}

QScrollBar:vertical {{ background: transparent; width: 10px; margin: 2px; }}
QScrollBar::handle:vertical {{ background: {c['tint']}; border-radius: 3px; min-height: 30px; }}
QScrollBar::handle:vertical:hover {{ background: {c['faint']}; }}
QScrollBar:horizontal {{ background: transparent; height: 10px; margin: 2px; }}
QScrollBar::handle:horizontal {{ background: {c['tint']}; border-radius: 3px; min-width: 30px; }}
QScrollBar::add-line, QScrollBar::sub-line {{ width: 0; height: 0; }}
QScrollBar::add-page, QScrollBar::sub-page {{ background: transparent; }}

QMenu {{ background: {c['pop']}; border: 1px solid {c['tint']}; border-radius: 10px; padding: 5px; }}
QMenu::item {{ padding: 6px 22px 6px 12px; border-radius: 6px; background: transparent; }}
QMenu::item:selected {{ background: {c['tint']}; }}
QMenu::item:disabled {{ color: {c['faint']}; }}
QMenu::separator {{ height: 1px; background: {c['tint']}; margin: 4px 8px; }}

QSplitter::handle {{ background: transparent; }}
QSplitter::handle:hover {{ background: {c['tint']}; }}
QGraphicsView {{ background: transparent; border: none; }}
QPlainTextEdit#Console {{
    background: transparent; border: none; font-family: "{CODE_FONT}"; font-size: {ui_size - 1}px;
    selection-background-color: {c['selection']};
}}
QProgressBar {{ background: {c['tint']}; border: none; border-radius: 3px; max-height: 6px; }}
QProgressBar::chunk {{ background: {c['accent']}; border-radius: 3px; }}
"""
