import re
import sys
import os

if len(sys.argv) > 1:
    files = [sys.argv[1]]
    
else:
    files = [f for f in os.listdir('.') if f.endswith(".tas")]

regex_1Line = re.compile(r' 4,D,[X|C]')
regex_2Line = re.compile(r' (4|1),D,[X|C]\s*(\d+)(.*)[X|C]')

def ZReplace(match):
    firstInputFrames = int(match.group(1))
    secondInputFrames = int(match.group(2))
    inputs = match.group(3)
    output = ' ' + str(firstInputFrames + secondInputFrames) + inputs + 'Z'
    return output

    
for filepath in files:
    file = open(filepath, 'r')
    text = file.read()
    
    replacedText = re.sub(regex_2Line, ZReplace, text)
    replacedText = re.sub(regex_1Line, ' 4,Z', replacedText)
    
    file.close()

    file = open(filepath, 'w')
    file.write(replacedText)
    file.close()
        
