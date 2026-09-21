# read_dialog.py — 读取 Unity 模态框的文本与按钮
import ctypes
from ctypes import wintypes

user32 = ctypes.windll.user32
WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
user32.EnumChildWindows.argtypes = [wintypes.HWND, WNDENUMPROC, wintypes.LPARAM]
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]

DIALOG = 2298382

def text_of(hwnd):
    txt = ctypes.create_unicode_buffer(1024)
    user32.GetWindowTextW(hwnd, txt, 1024)
    return txt.value

def enum_child(hwnd, lparam):
    cls = ctypes.create_unicode_buffer(128)
    user32.GetClassNameW(hwnd, cls, 128)
    print(cls.value, '|', repr(text_of(hwnd)))
    return True

print('dialog text:', repr(text_of(DIALOG)))
user32.EnumChildWindows(DIALOG, WNDENUMPROC(enum_child), 0)
