import re

def find_html_tags(file_path):
    text = open(file_path, encoding='utf-8').read()
    
    # Simple state machine to find text NOT in backticks
    in_backtick = False
    new_text = []
    for c in text:
        if c == '`':
            in_backtick = not in_backtick
        if not in_backtick:
            new_text.append(c)
    
    clean_text = "".join(new_text)
    
    tags = re.findall(r'<[^\s>]+>', clean_text)
    
    print(f'{file_path} tags outside backticks: {set(tags)}')

find_html_tags('docs/progress.md')
find_html_tags('docs/progress_archive.md')
