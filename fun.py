#!/usr/bin/env python3
# fun.py - chay kem moi lan AutoPush chay, cho vui :)
import os
import random
import datetime

JOKES = [
    "There are only 10 kinds of people: those who understand binary and those who don't.",
    "A SQL query walks into a bar, goes up to two tables and asks: can I JOIN you?",
    "Why do programmers prefer dark mode? Because light attracts bugs.",
    "It works on my machine. -- every developer, ever.",
    "Commit early, commit often. Push... let AutoPush handle that.",
    "99 little bugs in the code, take one down, patch it around, 127 little bugs in the code.",
    "To understand recursion, you must first understand recursion.",
    "Debugging: being the detective in a crime movie where you are also the murderer.",
    "There is no place like 127.0.0.1.",
    "Real programmers count from 0.",
]

ART = r"""
   _         _        ___         _
  /_\ _  _ _| |_ ___ | _ \_  _ __| |_
 / _ \ || |  _/ _ \| _/ || (_-< ' \
/_/ \_\_,_|\__\___/|_|  \_,_/__/_||_|
"""


def main():
    joke = random.choice(JOKES)
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(ART)
    print(f"[{now}] {joke}")

    log_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fun-log.txt")
    try:
        with open(log_path, "a", encoding="utf-8") as f:
            f.write(f"{now}  {joke}\n")
    except OSError:
        pass


if __name__ == "__main__":
    main()
