# frozen_string_literal: true

class CreateDehydratedBuildJoinTable < ActiveRecord::Migration[5.2]
  def change
    create_join_table :dehydrated_objects, :dev_builds

    add_index :dehydrated_objects_dev_builds, %i[dehydrated_object_id dev_build_id],
              unique: true, name: 'dehydrated_objects_dev_builds_index_compound'
  end
end
