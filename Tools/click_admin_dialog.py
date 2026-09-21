# click_admin_dialog.py — 枚举管理员确认对话框的子控件并点击“继续”按钮
import ctypes
from ctypes import wintypes
import time

user32 = ctypes.windll.user32
WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
user32.EnumChildWindows.argtypes = [wintypes.HWND, WNDENUMPROC, wintypes.LPARAM]
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.SendMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]

DLG = 920660

children = []
def enum_child(hwnd, lparam):
    cls = ctypes.create_unicode_buffer(128)
    txt = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, cls, 128)
    user32.GetWindowTextW(hwnd, txt, 256)
    children.append((hwnd, cls.value, txt.value))
    return True
user32.EnumChildWindows(DLG, WNDENUMPROC(enum_child), 0)
for c in children:
    print(c)

BM_CLICK = 0x00F5
clicked = False
for hwnd, cls, txt in children:
    if cls.lower() == 'button':
        user32.SendMessageW(hwnd, BM_CLICK, 0, 0)
        print('clicked button', hwnd, repr(txt))
        clicked = True
        time.sleep(1)
        break
if not clicked:
    print('no button found')
