class UserPolicy
  # User can see their own data
  regulate_instance_connections { self }

  # And they get their own stuff
  regulate_broadcast {|policy|
    # This doesn't work? Or maybe it does and the problem is the models not allowing access
    # policy.send_all_but(:password, :password_confirmation, :password_digest).to(self)
    policy.send_all_but(:password, :password_confirmation, :password_digest).to(policy.obj)
  }
end
