FROM mcr.microsoft.com/mssql/server:2022-latest
WORKDIR /init
COPY init.sh .
COPY seed.sh .
COPY schema/ schema/
COPY seed/ seed/
COPY upgrades/ upgrades/
USER root
RUN sed -i "s/\r$//" /init/init.sh /init/seed.sh
USER mssql
ENTRYPOINT ["/bin/bash", "/init/init.sh"]
