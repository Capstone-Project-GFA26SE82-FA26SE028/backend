FROM mcr.microsoft.com/mssql/server:2022-latest
WORKDIR /init
COPY . .
USER root
RUN sed -i "s/\r$//" /init/init.sh
USER mssql
ENTRYPOINT ["/bin/bash", "/init/init.sh"]
