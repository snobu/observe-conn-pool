#!/usr/bin/env python3

from asciichartpy import plot

import sys, time

series = [0]
chart_width = 70
chart_height = 40

cfg = {
    'height': chart_height,
    'floor': 0,
    'minimum': 0,
    'maximum': 40
}

for line in sys.stdin:
    try:
        value = int(line.rstrip())
    except:
        print('\n')
        for line in sys.stdin:
            print(line.rstrip())
        sys.exit(0)
 
    series.append(value)
    view = series[-chart_width:]
    print(chr(27) + "[2J") # clear screen
    print(f'{" " * round(chart_width/2-10)} TCP-ESTABLISHED DATABASE CONNECTIONS')
    print(plot(view, cfg))
    #time.sleep(0.07)
