@echo off
echo Deleting service...
sc stop HIDUPSResponder
sc delete HIDUPSResponder