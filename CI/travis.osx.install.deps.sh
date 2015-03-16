#!/bin/bash
set -ev

MONO_VER=3.6.0

brew update
which cmake || brew install cmake

wget "https://www.dropbox.com/s/zca1sfy7dic8apa/MonoFramework-MDK-4.0.0-therzok.macos10.xamarin.x86.pkg?dl=1" -O "MonoFramework-MDK-4.0.0-therzok.macos10.xamarin.x86.pkg"
sudo installer -pkg "MonoFramework-MDK-4.0.0-therzok.macos10.xamarin.x86.pkg" -target /

