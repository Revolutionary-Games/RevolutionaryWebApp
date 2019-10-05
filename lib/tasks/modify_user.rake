namespace :thrive do

  desc "Make an user admin"
  task :grant_admin, [:email] => [:environment] do |task, args|
    user = User.find_by email: args.email

    if !user
      raise RuntimeError.new "no user with email: #{args.email}"
    end

    user.admin = true
    user.save
  end

  desc "Revoke admin status"
  task :revoke_admin, [:email] => [:environment] do |task, args|
    user = User.find_by email: args.email

    if !user
      raise RuntimeError.new "no user with email: #{args.email}"
    end

    user.admin = false
    user.save
  end

  desc "Revoke user tokens"
  task :revoke_user_tokens, [:email] => [:environment] do |task, args|

    user = User.find_by email: args.email

    if !user
      raise RuntimeError.new "no user with email: #{args.email}"
    end

    user.reset_api_token
    user.reset_lfs_token
    user.save
  end

  # TODO: would be nice to have this to invalidate logins for example if the SSO
  # account is deleted
  # desc "Revoke user logins"
  # task :create_developer, [:email, :password] => [:environment] do |task, args|
  #   User.create! email: args[:email], password: args[:password], local: true, admin: false,
  #                password_confirmation: args[:password],
  #                developer: true
  # end
end
