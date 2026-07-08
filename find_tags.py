import re

def find_bare_tags(file_path):
    text = open(file_path, encoding='utf-8').read()
    # Find all <...>
    tags = re.finditer(r'<([^\s>]+)>', text)
    for match in tags:
        start = match.start()
        # count backticks before this tag
        backticks_before = text[:start].count('`')
        if backticks_before % 2 == 0:
            print(f'{file_path} Bare Tag: {match.group(0)}')

find_bare_tags('docs/progress.md')
find_bare_tags('docs/progress_archive.md')
