namespace :thrive do

  desc "Set a login password for account"
  task :set_password, [:email, :password] => [:environment] do |task, args|

    user = User.find_by email: args.email

    if !user
      raise RuntimeError.new "no user with email: #{args.email}"
    end

    if user.local
      user.password = args.password
      user.password_confirmation = args.password
      user.save
    else
      raise RuntimeError.new "can't set password on non local user"
    end
  end
end
