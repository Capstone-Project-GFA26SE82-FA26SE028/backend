FROM mcr.microsoft.com/mssql/server:2022-latest
WORKDIR /init
COPY . .
USER root
RUN find /init -type f \( -name "*.sh" -o -name "*.sql" \) -exec sed -i "s/\r$//" {} +
USER mssql
ENTRYPOINT ["/bin/bash", "/init/init.sh"]
