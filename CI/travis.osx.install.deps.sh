#!/bin/bash
set -ev

MONO_VER=3.6.0

brew update
which cmake || brew install cmake

wget "https://www.dropbox.com/s/c7zo5cldk7xylk0/MonoFramework-MDK-4.0.0.62.macos10.xamarin.x86.pkg?dl=1"
sudo installer -pkg "MonoFramework-MDK-4.0.0.62.macos10.xamarin.x86.pkg" -target /
