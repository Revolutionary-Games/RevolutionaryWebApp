namespace :thrive do

  desc "Create a new admin user with email and password"
  task :create_admin, [:email, :password] => [:environment] do |task, args|
    User.create! email: args[:email], password: args[:password], local: true, admin: true,
                 password_confirmation: args[:password],
                 developer: true
  end
end
