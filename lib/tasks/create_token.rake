namespace :thrive do

  desc "Creates a token for an user"
  task :create_token, [:email] => [:environment] do |task, args|

    user = User.find_by(email: args[:email])

    raise "no user found" if !user
    
    user.generate_api_token
    user.save

    puts "Generated token: #{user.api_token}"
  end
end
