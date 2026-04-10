#!/bin/bash
input=$(cat)
model=$(echo "$input" | jq -r '.model.display_name')
remaining=$(echo "$input" | jq -r '.context_window.remaining_percentage')
used=$(echo "$input" | jq -r '.context_window.used_percentage')

if [ -n "$remaining" ] && [ "$remaining" != "null" ]; then
    pct=$remaining
else
    pct=$used
fi

if [ -n "$pct" ] && [ "$pct" != "null" ]; then
    pct_int=${pct%.*}
    bar_len=20
    filled=$((pct_int * bar_len / 100))
    empty=$((bar_len - filled))
    
    printf "\033[2m%s\033[0m " "$model"
    printf "["
    for ((i=0; i<filled; i++)); do printf "\033[32m=\033[0m"; done
    for ((i=0; i<empty; i++)); do printf "\033[2m-\033[0m"; done
    printf "] %d%%" "$pct_int"
else
    printf "\033[2m%s\033[0m (context unknown)" "$model"
fi