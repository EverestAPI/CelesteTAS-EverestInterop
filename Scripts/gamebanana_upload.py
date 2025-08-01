import os
import sys
import requests
import json
import time
import hmac
import hashlib
import base64
import struct
import urllib.parse
from dataclasses import dataclass
from selenium import webdriver
from selenium.common.exceptions import NoSuchElementException
from selenium.webdriver.common.by import By
from selenium.webdriver.support.wait import WebDriverWait
from selenium.webdriver.firefox.options import Options

# Common User-Agent to pretent to be a real user
user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36"

def main():
    file_path = sys.argv[1]
    update_json_path = sys.argv[2]
    version_info_path = sys.argv[3]

    update_json = None
    with open(update_json_path, "r") as f:
        update_json = json.loads(f.read())

    celestetas_version = None
    studio_version = None
    with open(version_info_path, "r") as f:
        lines = f.readlines()
        celestetas_version = lines[0].strip()
        studio_version = lines[1].strip()

    # Setup browser
    options = Options()
    options.add_argument("--headless")

    profile = webdriver.FirefoxProfile()
    profile.set_preference("general.useragent.override", user_agent)

    driver = webdriver.Firefox(options=options)

    # Login
    driver.get("https://gamebanana.com/members/account/login")
    driver.implicitly_wait(5)
    time.sleep(5)

    # Remove cookie banner
    print("Removing cookie banner...", end="    ", flush=True)
    driver.execute_script("$('.fc-consent-root').remove()")
    print("Done", flush=True)
    driver.implicitly_wait(1)
    time.sleep(1)

    print("Performing username + password login...", end="    ", flush=True)
    driver.find_element(By.ID, "_sUsername").click()
    driver.find_element(By.ID, "_sUsername").send_keys(os.getenv("GAMEBANANA_USERNAME"))
    driver.find_element(By.ID, "_sPassword").click()
    driver.find_element(By.ID, "_sPassword").send_keys(os.getenv("GAMEBANANA_PASSWORD"))
    driver.execute_script("$('#UsernameLoginForm button').click()")
    print("Done", flush=True)

    driver.implicitly_wait(5)
    time.sleep(5)

    # Enter 2FA code if needed
    if driver.current_url == "https://gamebanana.com/members/account/login":
        print("Entering 2FA code...", end="    ", flush=True)
        driver.find_element(By.ID, "_nTotp").send_keys(compute_twofac_code(os.getenv("GAMEBANANA_2FA_URI")))
        print("Done", flush=True)

        driver.implicitly_wait(5)
        time.sleep(5)
    else:
        print(f"2FA not needed")

    is_tool = os.getenv('GAMEBANANA_ISTOOL') == "1"

    driver.get(f"https://gamebanana.com/{"tools" if is_tool else "mods"}/edit/{os.getenv('GAMEBANANA_MODID')}")
    driver.implicitly_wait(5)
    time.sleep(5)

    # Check exiting file count
    beforeFileCount = driver.execute_script("return $(\"fieldset[id='Files'] ul[id$='_UploadedFiles'] li\").length")

    if beforeFileCount >= 20:
        print("Deleting oldest file...", end="    ", flush=True)
        # Need to delete oldest file to have enough space
        driver.execute_script("$(\"fieldset[id='Files'] ul[id$='_UploadedFiles'] li:last button\").click()")

        wait = WebDriverWait(driver, timeout=2)
        alert = wait.until(lambda d : d.switch_to.alert)
        alert.accept()

        print("Done.", flush=True)
        driver.implicitly_wait(1)
        time.sleep(1)

    # Upload file
    print("Uploading new file...", end="    ", flush=True)
    driver.find_element(By.CSS_SELECTOR, "fieldset#Files input[id$='_FileInput']").send_keys(os.path.join(os.getcwd(), file_path))
    wait = WebDriverWait(driver, timeout=15, poll_frequency=.2)
    wait.until(lambda d : beforeFileCount != driver.execute_script("$(\"return fieldset[id='Files'] ul[id$='_UploadedFiles'] li\").length"))
    print("Done.", flush=True)
    driver.implicitly_wait(5)
    time.sleep(5)

    # Reorder to be the topmost
    print("Reordering new file to the top...", end="    ", flush=True)
    driver.execute_script("$(\"fieldset[id='Files'] ul[id$='_UploadedFiles'] li:last\").prependTo(\"fieldset[id='Files'] ul[id$='_UploadedFiles']\")")
    print("Done.", flush=True)
    driver.implicitly_wait(1)
    time.sleep(1)

    # Add description
    print("Adding description to file...", end="    ", flush=True)
    desc = f"CelesteTAS v{celestetas_version}, Studio v{studio_version}"
    driver.execute_script(f"$(\"fieldset[id='Files'] ul[id$='_UploadedFiles'] li:first .VersionInput\")[0].value = '{desc}'")
    print("Done.", flush=True)
    driver.implicitly_wait(1)
    time.sleep(1)

    # Store file ID
    file_id = driver.execute_script(f"return $(\"fieldset[id='Files'] ul[id$='_UploadedFiles'] li:first input[name='_idFileRow']\")[0].value")

    # Submit edit
    print("Submitting edit...", end="    ", flush=True)
    driver.execute_script("$('.Submit > button').click()")
    driver.implicitly_wait(15)
    time.sleep(15)
    print("Done.", flush=True)

    # Add update
    print("Adding update...", end="    ", flush=True)

    driver.execute_script(f"""
                            fetch("https://gamebanana.com/apiv11/{"Tool" if is_tool else "Mod"}/{os.getenv('GAMEBANANA_MODID')}/Update", {{
                               "credentials": "include",
                               "headers": {{
                                   "Accept": "application/json, text/plain, */*",
                                   "Accept-Language": "en,en-US;q=0.5",
                                   "Content-Type": "application/json",
                                   "Sec-Fetch-Dest": "empty",
                                   "Sec-Fetch-Mode": "cors",
                                   "Sec-Fetch-Site": "same-origin",
                                   "Sec-GPC": "1",
                                   "Priority": "u=0"
                               }},
                               "referrer": "https://gamebanana.com/{"tools" if is_tool else "mods"}/{os.getenv('GAMEBANANA_MODID')}",
                               "body": '{json.dumps({
                                   "_aChangeLog": update_json,
                                   "_aFileRowIds": [file_id],
                                   "_sName": f"CelesteTAS v{celestetas_version} / Studio v{studio_version}",
                                   "_sVersion": f"v{celestetas_version}",
                                }).replace("\\", "\\\\").replace("'", "\\'")}',
                               "method": "POST",
                               "mode": "cors"
                           }});
                           """)

    driver.implicitly_wait(5)
    time.sleep(5)
    print("Done.", flush=True)

    driver.quit()


def compute_twofac_code(uri: str) -> str:
    secret, period, digits, algorithm = parse_otpauth_uri(uri)

    # Get the current time step (based on period)
    current_time = int(time.time())
    time_step = current_time // period

    # Generate the TOTP token
    return get_totp_token(secret, time_step, digits, algorithm)

def parse_otpauth_uri(uri):
    # Parse the URI
    parsed_uri = urllib.parse.urlparse(uri)
    query_params = urllib.parse.parse_qs(parsed_uri.query)

    # Extract the secret and other parameters
    secret = query_params.get('secret', [None])[0]
    period = int(query_params.get('period', [30])[0])
    digits = int(query_params.get('digits', [6])[0])
    algorithm = query_params.get('algorithm', ['SHA1'])[0].upper()

    return secret, period, digits, algorithm

def base32_decode(encoded_secret):
    # Add padding if necessary
    missing_padding = len(encoded_secret) % 8
    if missing_padding != 0:
        encoded_secret += '=' * (8 - missing_padding)
    # Decode the base32 secret
    return base64.b32decode(encoded_secret.upper())

def get_totp_token(secret, time_step, digits=6, algorithm='SHA1'):
    # HMAC key is the decoded secret
    key = base32_decode(secret)

    # Convert time_step to bytes (8-byte integer)
    time_step_bytes = struct.pack('>Q', time_step)

    # Choose the hash function (default to SHA1)
    if algorithm == 'SHA1':
        hash_function = hashlib.sha1
    elif algorithm == 'SHA256':
        hash_function = hashlib.sha256
    elif algorithm == 'SHA512':
        hash_function = hashlib.sha512
    else:
        raise ValueError(f"Unsupported algorithm: {algorithm}")

    # Compute HMAC hash
    hmac_hash = hmac.new(key, time_step_bytes, hash_function).digest()

    # Extract dynamic binary code from HMAC hash
    offset = hmac_hash[-1] & 0x0F
    truncated_hash = hmac_hash[offset:offset + 4]
    truncated_hash = struct.unpack('>I', truncated_hash)[0] & 0x7FFFFFFF

    # Get the last 'digits' digits of the number
    totp_token = truncated_hash % (10 ** digits)

    return totp_token

if __name__ == "__main__":
    main()
