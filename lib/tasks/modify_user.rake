# frozen_string_literal: true

namespace :thrive do
  desc 'Make an user admin'
  task :grant_admin, [:email] => [:environment] do |_task, args|
    user = User.find_by email: args.email

    raise "no user with email: #{args.email}" unless user

    user.admin = true
    user.save
  end

  desc 'Revoke admin status'
  task :revoke_admin, [:email] => [:environment] do |_task, args|
    user = User.find_by email: args.email

    raise "no user with email: #{args.email}" unless user

    user.admin = false
    user.save
  end

  desc 'Revoke user tokens'
  task :revoke_user_tokens, [:email] => [:environment] do |_task, args|
    user = User.find_by email: args.email

    raise "no user with email: #{args.email}" unless user

    user.reset_api_token
    user.reset_lfs_token
    user.save
  end

  desc 'Update suspended status'
  task :set_suspended, %i[email suspended reason] => [:environment] do |_task, args|
    user = User.find_by email: args.email

    raise "no user with email: #{args.email}" unless user

    user.suspended = args.suspended
    user.suspended_reason = args.reason
    user.save!
  end
end
