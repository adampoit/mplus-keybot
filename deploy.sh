scp artifacts/* root@example.com:/srv/mplus-keybot-stage
scp mplus-keybot.service root@example.com:/etc/systemd/system

ssh -T root@example.com << EOF
	mkdir -p /srv/mplus-keybot

	sudo systemctl stop mplus-keybot.service
	mv /srv/mplus-keybot-stage/* /srv/mplus-keybot

	sudo systemctl daemon-reload
	sudo systemctl start mplus-keybot.service
	sudo systemctl status mplus-keybot.service
EOF
