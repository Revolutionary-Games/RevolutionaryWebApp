# frozen_string_literal: true

namespace :thrive do
  desc 'Create a new Git LFS project'
  task :create_lfs_project, %i[name slug public] => [:environment] do |_task, args|
    LfsProject.create! name: args[:name], slug: args[:slug],
                       public: ActiveModel::Type::Boolean.new.cast(args[:public])
  end
end
