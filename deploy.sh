ssh root@example.com 'mkdir -p /srv/mplus-keybot'

ssh root@example.com 'sudo systemctl stop mplus-keybot.service'

scp artifacts/* root@example.com:/srv/mplus-keybot
scp mplus-keybot.service root@example.com:/etc/systemd/system

ssh root@example.com 'sudo systemctl daemon-reload'
ssh root@example.com 'sudo systemctl start mplus-keybot.service'
ssh root@example.com 'sudo systemctl status mplus-keybot.service'