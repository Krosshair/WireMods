# Turns package/README.md into NEXUS_DESCRIPTION.txt (Nexus BBCode). Run from the mod folder.
import re
def links(t):
    out = ''; i = 0
    while i < len(t):
        if t[i] == '[':
            depth = 0; k = i
            while k < len(t):
                if t[k] == '[': depth += 1
                elif t[k] == ']':
                    depth -= 1
                    if depth == 0: break
                k += 1
            if k < len(t) - 1 and t[k + 1] == '(':
                d = 0; m = k + 1
                while m < len(t):
                    if t[m] == '(': d += 1
                    elif t[m] == ')':
                        d -= 1
                        if d == 0: break
                    m += 1
                out += '[url=' + t[k + 2:m] + ']' + t[i + 1:k] + '[/url]'; i = m + 1; continue
        out += t[i]; i += 1
    return out
def inline(t):
    t = links(t)
    t = re.sub(r'\*\*(.+?)\*\*', r'[b]\1[/b]', t)
    t = re.sub(r'(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])', r'[i]\1[/i]', t)
    return re.sub(r'`([^`]+)`', r'[font=Courier New]\1[/font]', t)
md = open('package/README.md', encoding='utf-8').read().replace('\r\n', '\n').split('\n')
out = []; inlist = False
for line in md:
    if line.startswith('- '):
        if not inlist: out.append('[list]'); inlist = True
        out.append('[*]' + inline(line[2:])); continue
    if inlist: out.append('[/list]'); inlist = False
    if line.startswith('# '): out.append('[size=6][b]' + inline(line[2:]) + '[/b][/size]')
    elif line.startswith('## '): out.append('[size=5][b]' + inline(line[3:]) + '[/b][/size]')
    elif line.startswith('### '): out.append('[size=4][b]' + inline(line[4:]) + '[/b][/size]')
    else: out.append(inline(line))
if inlist: out.append('[/list]')
open('NEXUS_DESCRIPTION.txt', 'w', encoding='utf-8', newline='\r\n').write('\n'.join(out))
