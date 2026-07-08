import re

def find_urls(file_path):
    text = open(file_path, encoding='utf-8').read()
    # Find all bare URLs that don't start with ( or ](
    urls = re.findall(r'(?<!\]\()(?<!\()(https?://[^\s<>\"\')]+)', text)
    if urls:
        print(f'{file_path} URLs: {urls}')
    
    # Also find TPolicy
    if '<TPolicy>' in text:
        print(f'{file_path} contains <TPolicy>')
        
find_urls('docs/progress.md')
find_urls('docs/progress_archive.md')
