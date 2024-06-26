#!/bin/bash

docker build -t alexisspencer/rdt-client:latest --no-cache https://github.com/lexiismadd/rdt-client.git#master && docker push alexisspencer/rdt-client:latest
