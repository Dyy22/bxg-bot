## MacOS From Source 

Open Terminal (if you don't know how to, click on the magnifying glass on the top right corner of your screen and type **Terminal** on the window that pops up) and navigate to the location where you want to install the bot (for example `cd ~`) 

##### Installing Homebrew and wget

*Skip this step if you already have homebrew installed*
- Copy and paste this command, then press Enter:  
    - `/usr/bin/ruby -e "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/master/install)"`  
- Install wget  
    - `brew install wget`

##### Installation Instructions

1. Download and run the **new** installer script `cd ~ && wget -N https://gitlab.com/Kwoth/nadeko-bash-installer/-/raw/master/linuxAIO.sh && bash linuxAIO.sh`
2. Install prerequisites (type `1` and press enter)
3. Download the bot (type `2` and press enter)
4. Exit the installer in order to set up your `creds.yml` 
5. Copy the creds.yml template 
    `cp nadekobot/output/creds_example.yml nadekobot/output/creds.yml` 
6. Open `nadekobot/output/creds.yml` with your favorite text editor. We will use nano here
    - `nano nadekobot/output/creds.yml`
7. [Enter your bot's token](#creds-guide)
    - After you're done, you can close nano (and save the file) by inputting, in order 
      - `CTRL`+`X`
      - `Y`
      - `Enter`
8. Run the bot (type `3` and press enter)

##### Update Instructions

1. ⚠ Stop the bot
2. Update and run the **new** installer script `cd ~ && wget -N https://gitlab.com/Kwoth/nadeko-bash-installer/-/raw/master/linuxAIO.sh && bash linuxAIO.sh`
3. Update the bot (type `2` and press enter)
4. Run the bot (type `3` and press enter)
5. 🎉 

## MacOS Manual Release installation instructions

##### Installation Instructions

1. Download the latest release from <https://gitlab.com/Kwoth/nadekobot/-/releases>
   - Look for the file called "X.XX.X-linux-x64-build.tar" (where X.XX.X is a series of numbers) and download it
2. Untar it 
   ⚠ Make sure that you change X.XX.X to the same series of numbers as in step 1!
    - `tar xf X.XX.X-linux-x64-build.tar`
3. Rename the `nadekobot-linux-x64` to `nadekobot` 
    - `mv nadekobot-linux-x64 nadekobot`
4. Move into nadekobot directory and make NadekoBot executable
    - `cd nadekobot && chmod +x NadekoBot`
5. Copy the creds.yml template 
    - `cp creds_example.yml creds.yml` 
6. Open `creds.yml` with your favorite text editor. We will use nano here
    - `nano nadekobot/output/creds.yml`
8. [Enter your bot's token](#creds-guide)
    - After you're done, you can close nano (and save the file) by inputting, in order 
       - `CTRL`+`X`
       - `Y`
       - `Enter`
9. Run the bot
    - `./NadekoBot`

##### Update Instructions

1. Stop the bot
2. Download the latest release from <https://gitlab.com/Kwoth/nadekobot/-/releases>
    - Look for the file called "X.XX.X-linux-x64-build.tar" (where X.XX.X is a series of numbers) and download it
3. Untar it 
   ⚠ Make sure that you change X.XX.X to the same series of numbers as in step 2!
    - `tar xf 2.99.8-linux-x64-build.tar`
4. Rename the old nadekobot directory to nadekobot-old (remove your old backup first if you have one, or back it up under a different name)
    - `rm -rf nadekobot-old 2>/dev/null`
    - `mv nadekobot nadekobot-old`
5. Rename the new nadekobot directory to nadekobot
    - `mv nadekobot-linux-x64 nadekobot`
6. Remove old strings and aliases to avoid overwriting the updated versions of those files  
   ⚠ If you've modified said files, back them up instead
    - `rm nadekobot-old/data/aliases.yml`
    - `rm -r nadekobot-old/data/strings`
7. Copy old data
    - `cp -RT nadekobot-old/data/ nadekobot/data/`
8. Copy creds.yml
    - `cp nadekobot-old/creds.yml nadekobot/`
9. Move into nadekobot directory and make the NadekoBot executable
    - `cd nadekobot && chmod +x NadekoBot`
10. Run the bot 
    - `./NadekoBot`

🎉 Enjoy

##### Steps 3 - 9 as a single command  

Don't forget to change X.XX.X to match step 2.
```sh
tar xf X.XX.X-linux-x64-build.tar && \
rm -rf nadekobot-old 2>/dev/null && \
mv nadekobot nadekobot-old && \
mv nadekobot-linux-x64 nadekobot && \
rm nadekobot-old/data/aliases.yml && \
rm -r nadekobot-old/data/strings && \
cp -RT nadekobot-old/data/ nadekobot/data/ && \
cp nadekobot-old/creds.yml nadekobot/ && \
cd nadekobot && chmod +x NadekoBot
```