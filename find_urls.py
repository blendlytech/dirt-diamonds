import re
text = open('docs/progress.md', encoding='utf-8').read()
# Find bare URLs that are not part of markdown links or image tags or angle brackets
links = re.findall(r'(?<!\]\()(?:https?|file)://[^\s<>\"\')]+', text)
print("progress.md URLs:", set(links))

text2 = open('docs/progress_archive.md', encoding='utf-8').read()
links2 = re.findall(r'(?<!\]\()(?:https?|file)://[^\s<>\"\')]+', text2)
print("progress_archive.md URLs:", set(links2))
