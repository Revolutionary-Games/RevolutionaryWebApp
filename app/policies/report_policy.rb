class ReportPolicy

  regulate_broadcast do |policy|

    # policy.send_all.to(AdminUser)

	# policy.send_all_but(:reporter_email).to(public ? Application : User)
    policy.send_all.to(public ? Application : User)
  end

end
