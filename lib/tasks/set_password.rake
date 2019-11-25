# frozen_string_literal: true

namespace :thrive do
  desc 'Set a login password for account'
  task :set_password, %i[email password] => [:environment] do |_task, args|
    user = User.find_by email: args.email

    raise "no user with email: #{args.email}" unless user

    if user.local
      user.password = args.password
      user.password_confirmation = args.password
      user.save!
    else
      raise "can't set password on non local user"
    end
  end
end
