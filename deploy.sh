ssh root@example.com 'mkdir -p /srv/mplus-keybot'
scp artifacts/* root@example.com:/srv/mplus-keybot
scp mplus-keybot.service root@example.com:/etc/systemd/system